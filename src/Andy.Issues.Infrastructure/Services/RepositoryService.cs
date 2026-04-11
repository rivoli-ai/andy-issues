// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Infrastructure.Services;

public class RepositoryService : IRepositoryService
{
    private readonly AppDbContext _db;
    private readonly IRepositoryAccessGuard _guard;
    private readonly IUserDirectory _userDirectory;
    private readonly IGitHubClient _gitHubClient;
    private readonly IAzureDevOpsClient _azureDevOpsClient;

    public RepositoryService(
        AppDbContext db,
        IRepositoryAccessGuard guard,
        IUserDirectory userDirectory,
        IGitHubClient gitHubClient,
        IAzureDevOpsClient azureDevOpsClient)
    {
        _db = db;
        _guard = guard;
        _userDirectory = userDirectory;
        _gitHubClient = gitHubClient;
        _azureDevOpsClient = azureDevOpsClient;
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
        await _db.SaveChangesAsync(ct);
        return (CreateRepositoryResult.Created, entity.ToDto());
    }

    public async Task<PagedResult<RepositoryDto>> ListAsync(
        string userId,
        RepositoryScope scope,
        int page,
        int pageSize,
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

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

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
                info = await _gitHubClient.GetRepositoryAsync(fullName, provider.AccessToken, ct);
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
                _db.Repositories.Add(new Repository
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = userId,
                    Name = info.Name,
                    Description = info.Description,
                    Provider = Andy.Issues.Domain.Enums.RepositoryProvider.GitHub,
                    CloneUrl = info.CloneUrl,
                    DefaultBranch = info.DefaultBranch,
                    ExternalId = info.ExternalId
                });
                added++;
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
                }
                else
                {
                    skipped++;
                }
            }
        }

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
        repo.AzureClientSecret = clientSecret;
        repo.AzureTenantId = tenantId;
        repo.AzureSubscriptionId = subscriptionId;
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

        // TODO (Story 4.1): exec `az login --service-principal ...` inside an
        // ephemeral helper container via ContainersClient and return the real
        // result. For now this is a presence check so the endpoint shape is
        // stable for frontend wiring.
        if (!repo.HasAzureIdentity)
            return new VerifyAzureIdentityResult(false, "No Azure identity configured.");

        return new VerifyAzureIdentityResult(
            true,
            "Azure identity fields present. Live verification against the Azure REST API is pending Story 4.1.");
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

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

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
                    organization, project, repoId, provider.AccessToken, ct);
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
                _db.Repositories.Add(new Repository
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = userId,
                    Name = info.Name,
                    Description = info.Description,
                    Provider = Andy.Issues.Domain.Enums.RepositoryProvider.AzureDevOps,
                    CloneUrl = info.CloneUrl,
                    DefaultBranch = info.DefaultBranch,
                    ExternalId = info.ExternalId
                });
                added++;
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
                }
                else
                {
                    skipped++;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        return new SyncResult(added, updated, skipped, errors);
    }
}
