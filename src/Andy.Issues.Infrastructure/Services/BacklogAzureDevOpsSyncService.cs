// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Services;

public class BacklogAzureDevOpsSyncService : IBacklogAzureDevOpsSyncService
{
    private readonly AppDbContext _db;
    private readonly IAzureDevOpsClient _client;
    private readonly IRepositoryAccessGuard _guard;
    private readonly IBoardNotifier _notifier;
    private readonly ILogger<BacklogAzureDevOpsSyncService> _logger;

    public BacklogAzureDevOpsSyncService(
        AppDbContext db,
        IAzureDevOpsClient client,
        IRepositoryAccessGuard guard,
        ILogger<BacklogAzureDevOpsSyncService> logger,
        IBoardNotifier? notifier = null)
    {
        _db = db;
        _client = client;
        _guard = guard;
        _logger = logger;
        _notifier = notifier ?? new NullBoardNotifier();
    }

    public async Task<SyncResult?> PushAsync(Guid repositoryId, string userId, CancellationToken ct = default)
    {
        if (!await _guard.CanViewAsync(repositoryId, userId, ct))
            return null;

        var repo = await _db.Repositories
            .Include(r => r.Epics).ThenInclude(e => e.Features).ThenInclude(f => f.Stories)
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
        if (repo is null) return null;

        if (repo.Provider != RepositoryProvider.AzureDevOps)
            return new SyncResult(0, 0, 0, new[] { "Repository is not linked to Azure DevOps." });

        if (!TryParseOrgProject(repo.CloneUrl, out var org, out var project))
            return new SyncResult(0, 0, 0, new[] { $"Cannot derive Azure DevOps org/project from clone URL '{repo.CloneUrl}'." });

        var pat = await GetPatAsync(userId, ct);
        if (pat is null)
            return new SyncResult(0, 0, 0, new[] { "No Azure DevOps linked provider for user." });

        int added = 0, updated = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var story in repo.Epics.SelectMany(e => e.Features).SelectMany(f => f.Stories))
        {
            var state = AzureDevOpsWorkItemMapping.ToAzureState(story.Status);
            var upsert = new AzureDevOpsWorkItemUpsert(
                story.AzureDevOpsWorkItemId,
                story.Title,
                story.Description,
                state);

            AzureDevOpsWorkItemSnapshot? snap;
            try
            {
                snap = await _client.UpsertWorkItemAsync(org, project, upsert, pat, ct);
            }
            catch (Exception ex)
            {
                errors.Add($"{story.Id}: {ex.Message}");
                continue;
            }

            if (snap is null)
            {
                errors.Add($"{story.Id}: upsert failed");
                continue;
            }

            if (story.AzureDevOpsWorkItemId is null)
            {
                story.AzureDevOpsWorkItemId = snap.Id;
                story.UpdatedAt = DateTimeOffset.UtcNow;
                added++;
            }
            else
            {
                story.UpdatedAt = DateTimeOffset.UtcNow;
                updated++;
            }
        }

        await _db.SaveChangesAsync(ct);
        return new SyncResult(added, updated, skipped, errors);
    }

    public async Task<SyncResult?> PullAsync(Guid repositoryId, string userId, CancellationToken ct = default)
    {
        if (!await _guard.CanViewAsync(repositoryId, userId, ct))
            return null;

        var repo = await _db.Repositories
            .Include(r => r.Epics).ThenInclude(e => e.Features).ThenInclude(f => f.Stories)
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
        if (repo is null) return null;

        if (repo.Provider != RepositoryProvider.AzureDevOps)
            return new SyncResult(0, 0, 0, new[] { "Repository is not linked to Azure DevOps." });

        if (!TryParseOrgProject(repo.CloneUrl, out var org, out var project))
            return new SyncResult(0, 0, 0, new[] { $"Cannot derive Azure DevOps org/project from clone URL '{repo.CloneUrl}'." });

        var pat = await GetPatAsync(userId, ct);
        if (pat is null)
            return new SyncResult(0, 0, 0, new[] { "No Azure DevOps linked provider for user." });

        var stories = repo.Epics.SelectMany(e => e.Features).SelectMany(f => f.Stories)
            .Where(s => s.AzureDevOpsWorkItemId.HasValue)
            .ToList();
        if (stories.Count == 0)
            return new SyncResult(0, 0, 0, Array.Empty<string>());

        var ids = stories.Select(s => s.AzureDevOpsWorkItemId!.Value).ToList();

        IReadOnlyList<AzureDevOpsWorkItemSnapshot> snaps;
        try
        {
            snaps = await _client.GetWorkItemsAsync(org, project, ids, pat, ct);
        }
        catch (Exception ex)
        {
            return new SyncResult(0, 0, 0, new[] { ex.Message });
        }

        var snapById = snaps.ToDictionary(s => s.Id);
        int updated = 0, skipped = 0;
        var errors = new List<string>();

        var transitioned = new List<UserStory>();
        foreach (var story in stories)
        {
            if (!snapById.TryGetValue(story.AzureDevOpsWorkItemId!.Value, out var snap))
            {
                skipped++;
                continue;
            }

            // AzDO is authoritative only for closed/done states — everything else preserves local.
            if (AzureDevOpsWorkItemMapping.IsDoneState(snap.State))
            {
                if (story.Status != UserStoryStatus.Done)
                {
                    try
                    {
                        story.SetStatus(UserStoryStatus.Done);
                        transitioned.Add(story);
                        updated++;
                    }
                    catch (InvalidOperationException ex)
                    {
                        errors.Add($"{story.Id}: {ex.Message}");
                    }
                }
                else
                {
                    skipped++;
                }
            }
            else
            {
                skipped++;
            }
        }

        if (updated > 0)
        {
            await _db.SaveChangesAsync(ct);
            foreach (var story in transitioned)
                await _notifier.StoryUpdatedAsync(repo.Id, story.ToDto(), ct);
        }
        return new SyncResult(0, updated, skipped, errors);
    }

    private async Task<string?> GetPatAsync(string userId, CancellationToken ct)
    {
        var provider = await _db.LinkedProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OwnerUserId == userId && p.Provider == LinkedProviderKind.AzureDevOps, ct);
        return provider?.AccessToken;
    }

    public static bool TryParseOrgProject(string cloneUrl, out string organization, out string project)
    {
        organization = project = string.Empty;
        if (string.IsNullOrWhiteSpace(cloneUrl)) return false;
        if (!Uri.TryCreate(cloneUrl, UriKind.Absolute, out var uri)) return false;

        // https://dev.azure.com/{org}/{project}/_git/{repo}
        // https://{org}@dev.azure.com/{org}/{project}/_git/{repo}
        if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 4 && segments[2] == "_git")
            {
                organization = segments[0];
                project = segments[1];
                return true;
            }
        }

        // https://{org}.visualstudio.com/{project}/_git/{repo}
        if (uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 3 && segments[1] == "_git")
            {
                organization = uri.Host[..^".visualstudio.com".Length];
                project = segments[0];
                return true;
            }
        }

        return false;
    }
}

public static class AzureDevOpsWorkItemMapping
{
    public static string ToAzureState(UserStoryStatus status) => status switch
    {
        UserStoryStatus.Draft => "New",
        UserStoryStatus.Ready => "Active",
        UserStoryStatus.InProgress => "Active",
        UserStoryStatus.InReview => "Resolved",
        UserStoryStatus.Done => "Closed",
        _ => "New"
    };

    public static bool IsDoneState(string azureState) =>
        azureState.Equals("Closed", StringComparison.OrdinalIgnoreCase)
        || azureState.Equals("Done", StringComparison.OrdinalIgnoreCase)
        || azureState.Equals("Removed", StringComparison.OrdinalIgnoreCase);
}
