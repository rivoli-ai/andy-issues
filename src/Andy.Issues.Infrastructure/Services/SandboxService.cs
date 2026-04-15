// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Services;

/// <summary>
/// Thin wrapper over <see cref="IContainersClient"/>. andy-issues never manages containers
/// directly — this service persists a minimal local projection (<see cref="Sandbox"/>) tying
/// a remote container id to a repository/branch/owner, and delegates every lifecycle call
/// to andy-containers.
/// </summary>
public class SandboxService : ISandboxService
{
    private const string DefaultTemplateCodeKey = "AndyContainers:DefaultTemplateCode";
    private const string FallbackTemplateCode = "ubuntu-dev";

    private readonly AppDbContext _db;
    private readonly IContainersClient _containers;
    private readonly IRepositoryAccessGuard _guard;
    private readonly IArtifactFeedService _artifactFeeds;
    private readonly IMcpConfigService _mcpConfigs;
    private readonly IConfiguration _config;
    private readonly ILogger<SandboxService> _logger;

    public SandboxService(
        AppDbContext db,
        IContainersClient containers,
        IRepositoryAccessGuard guard,
        IArtifactFeedService artifactFeeds,
        IMcpConfigService mcpConfigs,
        IConfiguration config,
        ILogger<SandboxService> logger)
    {
        _db = db;
        _containers = containers;
        _guard = guard;
        _artifactFeeds = artifactFeeds;
        _mcpConfigs = mcpConfigs;
        _config = config;
        _logger = logger;
    }

    public async Task<SandboxDto?> CreateAsync(
        CreateSandboxRequest request,
        string userId,
        CancellationToken ct = default)
    {
        if (!await _guard.CanViewAsync(request.RepositoryId, userId, ct))
            return null;

        var repo = await _db.Repositories.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.RepositoryId, ct);
        if (repo is null) return null;

        var templateCode = _config[DefaultTemplateCodeKey] ?? FallbackTemplateCode;
        var containerName = BuildContainerName(repo.Name, request.Branch);
        var environmentVariables = await BuildEnvironmentVariablesAsync(repo, userId, ct);

        ContainerInfo container;
        try
        {
            container = await _containers.CreateContainerAsync(
                containerName, templateCode, environmentVariables, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create container for repo {RepoId} branch {Branch}.",
                request.RepositoryId, request.Branch);
            throw;
        }

        var sandbox = new Sandbox
        {
            Id = Guid.NewGuid(),
            ContainerId = container.Id,
            RepositoryId = request.RepositoryId,
            OwnerUserId = userId,
            Branch = request.Branch,
            Status = ParseStatus(container.Status),
            IdeEndpoint = container.IdeEndpoint,
            VncEndpoint = container.VncEndpoint
        };
        _db.Sandboxes.Add(sandbox);
        _db.AppendSandboxEvent(sandbox, SandboxEventKind.Attached);
        await _db.SaveChangesAsync(ct);
        return sandbox.ToDto();
    }

    public async Task<IReadOnlyList<SandboxDto>> ListAsync(string userId, CancellationToken ct = default)
    {
        var locals = await _db.Sandboxes
            .Where(s => s.OwnerUserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        foreach (var sandbox in locals)
            await RefreshFromRemoteAsync(sandbox, ct);

        if (_db.ChangeTracker.HasChanges())
            await _db.SaveChangesAsync(ct);

        return locals.Select(s => s.ToDto()).ToList();
    }

    public async Task<SandboxDto?> GetAsync(Guid sandboxId, string userId, CancellationToken ct = default)
    {
        var sandbox = await _db.Sandboxes.FirstOrDefaultAsync(s => s.Id == sandboxId, ct);
        if (sandbox is null) return null;
        if (sandbox.OwnerUserId != userId) return null;

        await RefreshFromRemoteAsync(sandbox, ct);
        if (_db.ChangeTracker.HasChanges())
            await _db.SaveChangesAsync(ct);

        return sandbox.ToDto();
    }

    public async Task<bool> DestroyAsync(Guid sandboxId, string userId, CancellationToken ct = default)
    {
        var sandbox = await _db.Sandboxes.FirstOrDefaultAsync(s => s.Id == sandboxId, ct);
        if (sandbox is null) return false;
        if (sandbox.OwnerUserId != userId) return false;

        try
        {
            await _containers.DestroyContainerAsync(sandbox.ContainerId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to destroy container {ContainerId} for sandbox {SandboxId}.",
                sandbox.ContainerId, sandbox.Id);
            throw;
        }

        // Emit detached before removing the Sandbox row. EF holds the
        // entity in the ChangeTracker through SaveChanges so the payload
        // builder can still read its fields.
        _db.AppendSandboxEvent(sandbox, SandboxEventKind.Detached);
        _db.Sandboxes.Remove(sandbox);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<SandboxConnectionDto?> GetConnectionInfoAsync(
        Guid sandboxId,
        string userId,
        CancellationToken ct = default)
    {
        var sandbox = await _db.Sandboxes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sandboxId, ct);
        if (sandbox is null) return null;
        if (sandbox.OwnerUserId != userId) return null;

        var info = await _containers.GetConnectionInfoAsync(sandbox.ContainerId, ct);
        if (info is null) return null;
        return new SandboxConnectionDto(info.IdeEndpoint, info.VncEndpoint, info.SshEndpoint);
    }

    private async Task RefreshFromRemoteAsync(Sandbox sandbox, CancellationToken ct)
    {
        ContainerInfo? remote;
        try
        {
            remote = await _containers.GetContainerAsync(sandbox.ContainerId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh container {ContainerId} for sandbox {SandboxId}.",
                sandbox.ContainerId, sandbox.Id);
            return;
        }

        if (remote is null)
        {
            if (sandbox.Status != SandboxStatus.Destroyed)
            {
                sandbox.Status = SandboxStatus.Destroyed;
                sandbox.UpdatedAt = DateTimeOffset.UtcNow;
                // Remote container disappeared — emit detached so
                // consumers learn the sandbox is no longer live.
                _db.AppendSandboxEvent(sandbox, SandboxEventKind.Detached);
            }
            return;
        }

        var previousStatus = sandbox.Status;
        var newStatus = ParseStatus(remote.Status);
        var changed = false;
        if (sandbox.Status != newStatus) { sandbox.Status = newStatus; changed = true; }
        if (sandbox.IdeEndpoint != remote.IdeEndpoint) { sandbox.IdeEndpoint = remote.IdeEndpoint; changed = true; }
        if (sandbox.VncEndpoint != remote.VncEndpoint) { sandbox.VncEndpoint = remote.VncEndpoint; changed = true; }
        if (changed) sandbox.UpdatedAt = DateTimeOffset.UtcNow;

        // Transition-triggered events: only on first crossing into the
        // terminal state, to avoid spamming the same event on every
        // refresh tick.
        if (previousStatus != SandboxStatus.Failed && newStatus == SandboxStatus.Failed)
        {
            _db.AppendSandboxEvent(sandbox, SandboxEventKind.Failed,
                reason: $"container reported status '{remote.Status}'");
        }
        else if (previousStatus != SandboxStatus.Destroyed && newStatus == SandboxStatus.Destroyed)
        {
            _db.AppendSandboxEvent(sandbox, SandboxEventKind.Detached);
        }
    }

    private async Task AppendMcpServersAsync(
        Dictionary<string, string> vars,
        string userId,
        CancellationToken ct)
    {
        var configs = await _mcpConfigs.GetEnabledForUserAsync(userId, ct);
        if (configs.Count == 0) return;

        var serialized = configs
            .Select(c => new
            {
                id = c.Id,
                name = c.Name,
                description = c.Description,
                type = c.Type,
                command = c.Command,
                argumentsJson = c.ArgumentsJson,
                environmentJson = c.EnvironmentJson,
                url = c.Url,
                headersJson = c.HeadersJson
            })
            .ToList();
        vars["MCP_SERVERS_JSON"] = JsonSerializer.Serialize(serialized);
    }

    private static SandboxStatus ParseStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return SandboxStatus.Pending;
        if (Enum.TryParse<SandboxStatus>(status, ignoreCase: true, out var parsed))
            return parsed;
        return status.ToLowerInvariant() switch
        {
            "running" or "started" or "active" => SandboxStatus.Running,
            "stopped" or "exited" => SandboxStatus.Stopped,
            "failed" or "error" => SandboxStatus.Failed,
            "creating" or "pending" or "provisioning" => SandboxStatus.Creating,
            "destroying" or "removing" => SandboxStatus.Destroying,
            "destroyed" or "gone" => SandboxStatus.Destroyed,
            _ => SandboxStatus.Pending
        };
    }

    public static IReadOnlyDictionary<string, string>? BuildEnvironmentVariables(Domain.Entities.Repository repo)
    {
        var vars = new Dictionary<string, string>();
        AppendAzureIdentity(vars, repo);
        return vars.Count == 0 ? null : vars;
    }

    internal async Task<IReadOnlyDictionary<string, string>?> BuildEnvironmentVariablesAsync(
        Domain.Entities.Repository repo,
        string userId,
        CancellationToken ct)
    {
        var vars = new Dictionary<string, string>();
        AppendAzureIdentity(vars, repo);
        await AppendArtifactFeedsAsync(vars, userId, ct);
        await AppendMcpServersAsync(vars, userId, ct);
        return vars.Count == 0 ? null : vars;
    }

    private static void AppendAzureIdentity(Dictionary<string, string> vars, Domain.Entities.Repository repo)
    {
        if (!repo.HasAzureIdentity) return;
        vars["AZURE_CLIENT_ID"] = repo.AzureClientId!;
        vars["AZURE_CLIENT_SECRET"] = repo.AzureClientSecret!;
        vars["AZURE_TENANT_ID"] = repo.AzureTenantId!;
        if (!string.IsNullOrEmpty(repo.AzureSubscriptionId))
            vars["AZURE_SUBSCRIPTION_ID"] = repo.AzureSubscriptionId;
    }

    private async Task AppendArtifactFeedsAsync(
        Dictionary<string, string> vars,
        string userId,
        CancellationToken ct)
    {
        var feeds = await _artifactFeeds.GetEnabledAsync(ct);
        if (feeds.Count == 0) return;

        var serialized = feeds
            .Select(f => new
            {
                name = f.Name,
                type = f.Type,
                organization = f.Organization,
                project = f.Project,
                feedName = f.FeedName
            })
            .ToList();
        vars["ARTIFACT_FEEDS_JSON"] = JsonSerializer.Serialize(serialized);

        var pat = await _db.LinkedProviders
            .AsNoTracking()
            .Where(p => p.OwnerUserId == userId && p.Provider == LinkedProviderKind.AzureDevOps)
            .Select(p => p.AccessToken)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrEmpty(pat))
            vars["AZURE_DEVOPS_PAT"] = pat;
    }

    private static string BuildContainerName(string repoName, string branch)
    {
        var safeRepo = string.Concat((repoName ?? "repo").Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        var safeBranch = string.Concat((branch ?? "main").Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"andy-sbx-{safeRepo}-{safeBranch}-{suffix}".ToLowerInvariant();
        return name.Length > 63 ? name[..63] : name;
    }
}
