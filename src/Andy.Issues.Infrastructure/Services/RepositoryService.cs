// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Services;

public class RepositoryService : IRepositoryService
{
    private readonly AppDbContext _db;
    private readonly IRepositoryAccessGuard _guard;
    private readonly IUserDirectory _userDirectory;
    private readonly IGitHubClient _gitHubClient;
    private readonly IAzureDevOpsClient _azureDevOpsClient;
    private readonly ICodeIndexClient _codeIndexClient;
    private readonly ISecretStore _secretStore;
    private readonly ILogger<RepositoryService> _logger;

    public RepositoryService(
        AppDbContext db,
        IRepositoryAccessGuard guard,
        IUserDirectory userDirectory,
        IGitHubClient gitHubClient,
        IAzureDevOpsClient azureDevOpsClient,
        ICodeIndexClient codeIndexClient,
        ISecretStore secretStore,
        ILogger<RepositoryService> logger)
    {
        _db = db;
        _guard = guard;
        _userDirectory = userDirectory;
        _gitHubClient = gitHubClient;
        _azureDevOpsClient = azureDevOpsClient;
        _codeIndexClient = codeIndexClient;
        _secretStore = secretStore;
        _logger = logger;
    }

    public async Task<(CreateRepositoryResult Result, RepositoryDto? Dto)> CreateAsync(
        CreateRepositoryRequest request,
        string ownerUserId,
        CancellationToken ct = default)
    {
        // Provider parsing is case-insensitive so callers can pass
        // "github" / "GitHub" / "GITHUB" interchangeably. The legacy
        // sync-github / sync-azure endpoints take the provider
        // implicitly via the route name; here it has to be explicit
        // because both providers go through the same endpoint.
        if (!Enum.TryParse<RepositoryProvider>(request.Provider, ignoreCase: true, out var provider))
            return (CreateRepositoryResult.InvalidProvider, null);

        // Reject empty/whitespace clone URLs and anything that isn't an
        // absolute http(s) URL. We do not try to be exhaustive — the
        // git client at sandbox/sync time will catch deeper issues.
        if (string.IsNullOrWhiteSpace(request.CloneUrl)
            || !Uri.TryCreate(request.CloneUrl, UriKind.Absolute, out var parsedUrl)
            || (parsedUrl.Scheme != Uri.UriSchemeHttp && parsedUrl.Scheme != Uri.UriSchemeHttps))
        {
            return (CreateRepositoryResult.InvalidCloneUrl, null);
        }

        // Idempotency: if the same owner already has a repo at the
        // same clone URL, surface a conflict instead of creating a
        // duplicate row. The Conductor sheet treats this as a benign
        // outcome ("already added").
        var existing = await _db.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.OwnerUserId == ownerUserId && r.CloneUrl == request.CloneUrl,
                ct);
        if (existing is not null)
            return (CreateRepositoryResult.AlreadyExists, existing.ToDto());

        var entity = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Name = request.Name,
            Description = request.Description,
            Provider = provider,
            CloneUrl = request.CloneUrl,
            DefaultBranch = string.IsNullOrWhiteSpace(request.DefaultBranch) ? "main" : request.DefaultBranch,
            ExternalId = request.ExternalId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Repositories.Add(entity);
        _db.AppendRepositoryRegistered(entity);
        await _db.SaveChangesAsync(ct);

        // Best-effort code index registration — don't fail repo creation
        // if andy-code-index is down or not configured.
        try
        {
            var regResult = await _codeIndexClient.RegisterAsync(entity.CloneUrl, entity.DefaultBranch, ct);
            if (regResult.Outcome is CodeIndexRegistrationOutcome.Registered
                or CodeIndexRegistrationOutcome.AlreadyRegistered)
            {
                entity.CodeIndexStatus = CodeIndexStatus.Indexing;
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Code index registration failed for {CloneUrl}; repo creation succeeded.", entity.CloneUrl);
        }

        return (CreateRepositoryResult.Created, entity.ToDto());
    }

    public async Task<PagedResult<RepositoryDto>> ListAsync(
        string userId,
        RepositoryScope scope,
        int page,
        int pageSize,
        Andy.Issues.Domain.Enums.RepositoryProvider? provider = null,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        IQueryable<Repository> query = _db.Repositories.AsNoTracking();

        query = scope switch
        {
            RepositoryScope.Mine => query.Where(r => r.OwnerUserId == userId),
            RepositoryScope.Shared => query
                .Where(r => r.OwnerUserId != userId
                    && r.Shares.Any(s => s.SharedWithUserId == userId)),
            RepositoryScope.All => query.Where(r => r.OwnerUserId == userId
                || r.Shares.Any(s => s.SharedWithUserId == userId)),
            _ => query.Where(r => r.OwnerUserId == userId)
        };

        // #100 — optional provider filter. Predicate composes on top of
        // scope so the count + page reflect the filtered set.
        if (provider is { } p)
            query = query.Where(r => r.Provider == p);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<RepositoryDto>(
            items.Select(r => r.ToDto()).ToList(),
            page,
            pageSize,
            total);
    }

    public async Task<RepositoryDto?> GetAsync(Guid id, string userId, CancellationToken ct = default)
    {
        if (!await _guard.CanViewAsync(id, userId, ct))
            return null;

        var repo = await _db.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        return repo?.ToDto();
    }

    public async Task<bool> DeleteAsync(Guid id, string userId, CancellationToken ct = default)
    {
        if (!await _guard.IsOwnerAsync(id, userId, ct))
            return false;

        var repo = await _db.Repositories
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (repo is null) return false;

        // Backlog (epics → features → stories), shares, and sandbox projection
        // rows cascade via EF relationships configured in AppDbContext. Live
        // container destruction against andy-containers is wired up in
        // Story 4.1 when SandboxService exists.
        _db.Repositories.Remove(repo);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(ShareResult Result, RepositoryShareDto? Dto)> ShareAsync(
        Guid repositoryId,
        string email,
        string ownerUserId,
        CancellationToken ct = default)
    {
        if (!await _guard.IsOwnerAsync(repositoryId, ownerUserId, ct))
        {
            var exists = await _db.Repositories
                .AsNoTracking()
                .AnyAsync(r => r.Id == repositoryId, ct);
            return (exists ? ShareResult.NotOwner : ShareResult.NotFound, null);
        }

        var target = await _userDirectory.FindByEmailAsync(email, ct);
        if (target is null)
            return (ShareResult.EmailNotFound, null);

        if (target.UserId == ownerUserId)
            return (ShareResult.SelfShareRejected, null);

        var existing = await _db.RepositoryShares
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.RepositoryId == repositoryId && s.SharedWithUserId == target.UserId,
                ct);
        if (existing is not null)
            return (ShareResult.AlreadyShared, existing.ToDto());

        var share = new RepositoryShare
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            SharedWithUserId = target.UserId,
            GrantedByUserId = ownerUserId,
            GrantedAt = DateTimeOffset.UtcNow
        };
        _db.RepositoryShares.Add(share);
        await _db.SaveChangesAsync(ct);
        return (ShareResult.Created, share.ToDto());
    }

    public async Task<bool> UnshareAsync(
        Guid repositoryId,
        string targetUserId,
        string ownerUserId,
        CancellationToken ct = default)
    {
        if (!await _guard.IsOwnerAsync(repositoryId, ownerUserId, ct))
            return false;

        var share = await _db.RepositoryShares
            .FirstOrDefaultAsync(
                s => s.RepositoryId == repositoryId && s.SharedWithUserId == targetUserId,
                ct);
        if (share is null) return false;

        _db.RepositoryShares.Remove(share);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<RepositoryShareDto>?> ListSharesAsync(
        Guid repositoryId,
        string ownerUserId,
        CancellationToken ct = default)
    {
        if (!await _guard.IsOwnerAsync(repositoryId, ownerUserId, ct))
            return null;

        var shares = await _db.RepositoryShares
            .AsNoTracking()
            .Where(s => s.RepositoryId == repositoryId)
            .OrderBy(s => s.GrantedAt)
            .ToListAsync(ct);

        return shares.Select(s => s.ToDto()).ToList();
    }

    // #99 — repos available at the upstream provider but not yet
    // synced into andy-issues for the caller. Today only GitHub is
    // wired; AzureDevOps support will land alongside its different
    // request shape (org + project scoping required).
    public async Task<PagedResult<AvailableRepositoryDto>?> ListAvailableAsync(
        string userId,
        Andy.Issues.Domain.Enums.RepositoryProvider provider,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        if (provider != Andy.Issues.Domain.Enums.RepositoryProvider.GitHub)
        {
            // AzureDevOps follow-up — org+project params haven't been
            // wired into the surface yet. Return null so callers see a
            // distinct "no linked provider" / "unsupported" outcome
            // rather than an empty page.
            return null;
        }

        var linked = await _db.LinkedProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OwnerUserId == userId
                && p.Provider == Andy.Issues.Domain.Enums.LinkedProviderKind.GitHub, ct);
        if (linked is null) return null;

        var accessToken = await _secretStore.ResolveAsync(linked.AccessToken, ct) ?? linked.AccessToken;

        // GitHub paginates server-side; we ask for at most pageSize+10
        // so we can drop already-synced rows and still serve a
        // pageSize-sized page when the user has a small number of
        // already-imported repos. Larger overshoot would mean an
        // extra GitHub call only when the user has many sync'd repos
        // — acceptable for v1.
        var fetched = await _gitHubClient.ListUserRepositoriesAsync(
            accessToken, search, page, perPage: pageSize, ct);

        var alreadyOwnedIds = await _db.Repositories
            .AsNoTracking()
            .Where(r => r.OwnerUserId == userId
                && r.Provider == Andy.Issues.Domain.Enums.RepositoryProvider.GitHub
                && r.ExternalId != null)
            .Select(r => r.ExternalId!)
            .ToListAsync(ct);
        var ownedSet = alreadyOwnedIds.ToHashSet(StringComparer.Ordinal);

        var available = fetched
            .Where(r => !ownedSet.Contains(r.ExternalId))
            .Select(r => new AvailableRepositoryDto(
                ExternalId: r.ExternalId,
                Name: r.Name,
                FullName: r.FullName,
                Description: r.Description,
                CloneUrl: r.CloneUrl,
                DefaultBranch: r.DefaultBranch,
                Provider: "GitHub"))
            .ToList();

        // GitHub's list endpoint doesn't return a total count for
        // paginated responses. Returning the page-sized total is the
        // honest answer; callers loop until they get a short page.
        return new PagedResult<AvailableRepositoryDto>(
            available, page, pageSize, available.Count);
    }

    public async Task<SyncResult?> SyncFromGitHubAsync(
        string userId,
        IReadOnlyList<string> fullNames,
        CancellationToken ct = default)
    {
        var provider = await _db.LinkedProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OwnerUserId == userId
                && p.Provider == Andy.Issues.Domain.Enums.LinkedProviderKind.GitHub, ct);
        if (provider is null)
            return null;

        var accessToken = await _secretStore.ResolveAsync(provider.AccessToken, ct) ?? provider.AccessToken;

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();
        var touched = new List<Repository>();

        foreach (var fullName in fullNames.Distinct())
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                skipped++;
                continue;
            }

            GitHubRepositoryInfo? info;
            try
            {
                info = await _gitHubClient.GetRepositoryAsync(fullName, accessToken, ct);
            }
            catch (Exception ex)
            {
                errors.Add($"{fullName}: {ex.Message}");
                continue;
            }

            if (info is null)
            {
                errors.Add($"{fullName}: not found");
                continue;
            }

            var existing = await _db.Repositories
                .FirstOrDefaultAsync(
                    r => r.OwnerUserId == userId
                        && r.Provider == Andy.Issues.Domain.Enums.RepositoryProvider.GitHub
                        && r.ExternalId == info.ExternalId,
                    ct);

            if (existing is null)
            {
                var newRepo = new Repository
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = userId,
                    Name = info.Name,
                    Description = info.Description,
                    Provider = Andy.Issues.Domain.Enums.RepositoryProvider.GitHub,
                    CloneUrl = info.CloneUrl,
                    DefaultBranch = info.DefaultBranch,
                    ExternalId = info.ExternalId
                };
                _db.Repositories.Add(newRepo);
                added++;
                touched.Add(newRepo);
            }
            else
            {
                var changed = false;
                if (existing.Name != info.Name) { existing.Name = info.Name; changed = true; }
                if (existing.Description != info.Description) { existing.Description = info.Description; changed = true; }
                if (existing.CloneUrl != info.CloneUrl) { existing.CloneUrl = info.CloneUrl; changed = true; }
                if (existing.DefaultBranch != info.DefaultBranch) { existing.DefaultBranch = info.DefaultBranch; changed = true; }
                if (changed)
                {
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                    updated++;
                    touched.Add(existing);
                }
                else
                {
                    skipped++;
                }
            }
        }

        // One synced event per repo row that was added or updated. Skipped
        // rows (no-op) and repos that errored out do not emit. Payload
        // carries the batch-level aggregate so a consumer can see the
        // whole sync at a glance even if it only subscribes to a single
        // repo's events.
        foreach (var repo in touched)
            _db.AppendRepositorySynced(repo, added, updated, skipped, errors.Count);

        await _db.SaveChangesAsync(ct);
        return new SyncResult(added, updated, skipped, errors);
    }

    public async Task<SetAzureIdentityResult> SetAzureIdentityAsync(
        Guid repositoryId,
        string clientId,
        string clientSecret,
        string tenantId,
        string? subscriptionId,
        string ownerUserId,
        CancellationToken ct = default)
    {
        var repo = await _db.Repositories
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
        if (repo is null) return SetAzureIdentityResult.NotFound;
        if (repo.OwnerUserId != ownerUserId) return SetAzureIdentityResult.NotOwner;

        repo.AzureClientId = clientId;
        repo.AzureClientSecret = await _secretStore.StoreAsync(
            $"andy.issues.repo.{repositoryId}.azureClientSecret", clientSecret, ct);
        repo.AzureTenantId = tenantId;
        repo.AzureSubscriptionId = subscriptionId;

        // A repo has one Azure identity at a time. Setting the
        // service-principal tuple clears any previously-saved PAT so the
        // verify path can't be fooled into taking the wrong branch.
        repo.AzureOrganization = null;
        repo.AzureProject = null;
        repo.AzurePat = null;

        repo.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return SetAzureIdentityResult.Updated;
    }

    public async Task<SetAzureIdentityResult> SetAzurePatIdentityAsync(
        Guid repositoryId,
        string organization,
        string project,
        string pat,
        string ownerUserId,
        CancellationToken ct = default)
    {
        var repo = await _db.Repositories
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
        if (repo is null) return SetAzureIdentityResult.NotFound;
        if (repo.OwnerUserId != ownerUserId) return SetAzureIdentityResult.NotOwner;

        repo.AzureOrganization = organization;
        repo.AzureProject = project;
        repo.AzurePat = await _secretStore.StoreAsync(
            $"andy.issues.repo.{repositoryId}.azurePat", pat, ct);

        // Mirror of the service-principal path: setting the PAT clears the
        // service-principal tuple so exactly one identity is ever live.
        repo.AzureClientId = null;
        repo.AzureClientSecret = null;
        repo.AzureTenantId = null;
        repo.AzureSubscriptionId = null;

        repo.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return SetAzureIdentityResult.Updated;
    }

    public async Task<VerifyAzureIdentityResult?> VerifyAzureIdentityAsync(
        Guid repositoryId,
        string ownerUserId,
        CancellationToken ct = default)
    {
        var repo = await _db.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
        if (repo is null) return null;
        if (repo.OwnerUserId != ownerUserId) return null;

        switch (repo.HasAzureIdentityKind)
        {
            case AzureIdentityKind.None:
                return new VerifyAzureIdentityResult(false, "No Azure identity configured.");

            case AzureIdentityKind.ServicePrincipal:
                // TODO (Story 4.1): exec `az login --service-principal ...` inside an
                // ephemeral helper container via ContainersClient and return the real
                // result. For now this is a presence check so the endpoint shape is
                // stable for frontend wiring.
                return new VerifyAzureIdentityResult(
                    true,
                    "Azure service-principal fields present. Live verification against the Azure REST API is pending Story 4.1.");

            case AzureIdentityKind.Pat:
                var pat = await _secretStore.ResolveAsync(repo.AzurePat, ct) ?? repo.AzurePat ?? string.Empty;
                var connection = await _azureDevOpsClient.VerifyConnectionAsync(
                    repo.AzureOrganization!, pat, ct);
                if (connection is null || string.IsNullOrEmpty(connection.AuthenticatedUserId))
                {
                    return new VerifyAzureIdentityResult(
                        false,
                        $"Azure DevOps PAT verification failed for organization '{repo.AzureOrganization}'.");
                }
                return new VerifyAzureIdentityResult(
                    true,
                    $"Azure DevOps PAT authenticated as '{connection.DisplayName}'.");

            default:
                return new VerifyAzureIdentityResult(false, "Unknown Azure identity kind.");
        }
    }

    public async Task<SetLlmResult> SetLlmSettingAsync(
        Guid repositoryId,
        Guid? llmSettingId,
        string ownerUserId,
        CancellationToken ct = default)
    {
        var repo = await _db.Repositories
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
        if (repo is null) return SetLlmResult.RepositoryNotFound;
        if (repo.OwnerUserId != ownerUserId) return SetLlmResult.NotOwner;

        if (llmSettingId is not null)
        {
            var llmExists = await _db.LlmSettings
                .AsNoTracking()
                .AnyAsync(l => l.Id == llmSettingId && l.OwnerUserId == ownerUserId, ct);
            if (!llmExists) return SetLlmResult.LlmSettingNotFound;
        }

        repo.LlmSettingId = llmSettingId;
        repo.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return SetLlmResult.Updated;
    }

    public async Task<SyncResult?> SyncFromAzureDevOpsAsync(
        string userId,
        string organization,
        string? project,
        IReadOnlyList<string> repositoryIds,
        CancellationToken ct = default)
    {
        var provider = await _db.LinkedProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OwnerUserId == userId
                && p.Provider == Andy.Issues.Domain.Enums.LinkedProviderKind.AzureDevOps, ct);
        if (provider is null)
            return null;

        if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(project))
            return new SyncResult(0, 0, 0, new[] { "organization and project are required" });

        var azAccessToken = await _secretStore.ResolveAsync(provider.AccessToken, ct) ?? provider.AccessToken;

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();
        var touched = new List<Repository>();

        foreach (var repoId in repositoryIds.Distinct())
        {
            if (string.IsNullOrWhiteSpace(repoId))
            {
                skipped++;
                continue;
            }

            AzureDevOpsRepositoryInfo? info;
            try
            {
                info = await _azureDevOpsClient.GetRepositoryAsync(
                    organization, project, repoId, azAccessToken, ct);
            }
            catch (Exception ex)
            {
                errors.Add($"{repoId}: {ex.Message}");
                continue;
            }

            if (info is null)
            {
                errors.Add($"{repoId}: not found");
                continue;
            }

            var existing = await _db.Repositories
                .FirstOrDefaultAsync(
                    r => r.OwnerUserId == userId
                        && r.Provider == Andy.Issues.Domain.Enums.RepositoryProvider.AzureDevOps
                        && r.ExternalId == info.ExternalId,
                    ct);

            if (existing is null)
            {
                var newRepo = new Repository
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = userId,
                    Name = info.Name,
                    Description = info.Description,
                    Provider = Andy.Issues.Domain.Enums.RepositoryProvider.AzureDevOps,
                    CloneUrl = info.CloneUrl,
                    DefaultBranch = info.DefaultBranch,
                    ExternalId = info.ExternalId
                };
                _db.Repositories.Add(newRepo);
                added++;
                touched.Add(newRepo);
            }
            else
            {
                var changed = false;
                if (existing.Name != info.Name) { existing.Name = info.Name; changed = true; }
                if (existing.Description != info.Description) { existing.Description = info.Description; changed = true; }
                if (existing.CloneUrl != info.CloneUrl) { existing.CloneUrl = info.CloneUrl; changed = true; }
                if (existing.DefaultBranch != info.DefaultBranch) { existing.DefaultBranch = info.DefaultBranch; changed = true; }
                if (changed)
                {
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                    updated++;
                    touched.Add(existing);
                }
                else
                {
                    skipped++;
                }
            }
        }

        foreach (var repo in touched)
            _db.AppendRepositorySynced(repo, added, updated, skipped, errors.Count);

        await _db.SaveChangesAsync(ct);
        return new SyncResult(added, updated, skipped, errors);
    }
}
