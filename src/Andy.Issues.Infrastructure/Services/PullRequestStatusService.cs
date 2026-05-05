// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Application.PullRequests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Services;

public class PullRequestStatusService : IPullRequestStatusService
{
    private readonly AppDbContext _db;
    private readonly IRepositoryAccessGuard _guard;
    private readonly IGitHubClient _gitHubClient;
    private readonly IAzureDevOpsClient _azureDevOpsClient;
    private readonly ISecretStore _secretStore;
    private readonly IBoardNotifier _notifier;
    private readonly ILogger<PullRequestStatusService> _logger;

    public PullRequestStatusService(
        AppDbContext db,
        IRepositoryAccessGuard guard,
        IGitHubClient gitHubClient,
        IAzureDevOpsClient azureDevOpsClient,
        ISecretStore secretStore,
        IBoardNotifier notifier,
        ILogger<PullRequestStatusService> logger)
    {
        _db = db;
        _guard = guard;
        _gitHubClient = gitHubClient;
        _azureDevOpsClient = azureDevOpsClient;
        _secretStore = secretStore;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<StoryPullRequestStatusDto?> CheckStoryAsync(
        Guid storyId,
        string userId,
        CancellationToken ct = default)
    {
        var story = await _db.UserStories
            .Include(s => s.Feature).ThenInclude(f => f.Epic)
            .FirstOrDefaultAsync(s => s.Id == storyId, ct);
        if (story is null) return null;
        if (!await _guard.CanViewAsync(story.Feature.Epic.RepositoryId, userId, ct))
            return null;

        if (string.IsNullOrWhiteSpace(story.PullRequestUrl))
        {
            return new StoryPullRequestStatusDto(
                StoryId: story.Id,
                HasPr: false,
                PrUrl: null,
                PrState: null,
                PrMerged: null,
                PrMergedAt: null,
                StoryStatus: story.Status.ToString(),
                StatusUpdated: false);
        }

        var status = await ResolveStatusAsync(story.PullRequestUrl, userId, ct);

        var statusUpdated = false;
        if (status is { Merged: true } && story.Status != UserStoryStatus.Done)
        {
            ApplyDoneTransition(story);
            await _db.SaveChangesAsync(ct);
            await _notifier.StoryUpdatedAsync(
                story.Feature.Epic.RepositoryId, story.ToDto(), ct);
            statusUpdated = true;
        }

        return new StoryPullRequestStatusDto(
            StoryId: story.Id,
            HasPr: true,
            PrUrl: story.PullRequestUrl,
            PrState: status?.State,
            PrMerged: status?.Merged,
            PrMergedAt: status?.MergedAt,
            StoryStatus: story.Status.ToString(),
            StatusUpdated: statusUpdated);
    }

    public async Task<SyncPullRequestStatusesResultDto?> SyncRepositoryAsync(
        Guid repositoryId,
        string userId,
        CancellationToken ct = default)
    {
        if (!await _guard.CanViewAsync(repositoryId, userId, ct))
            return null;

        var stories = await _db.UserStories
            .Include(s => s.Feature).ThenInclude(f => f.Epic)
            .Where(s => s.Feature.Epic.RepositoryId == repositoryId
                && s.PullRequestUrl != null
                && s.PullRequestUrl != "")
            .ToListAsync(ct);

        var updates = new List<UpdatedStoryDto>();
        var notifyDtos = new List<UserStoryDto>();

        foreach (var story in stories)
        {
            PullRequestStatusInfo? status;
            try
            {
                status = await ResolveStatusAsync(story.PullRequestUrl!, userId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-story failure shouldn't abort the batch — Conductor
                // calls this on every backlog load and a single broken PR
                // URL must not poison the rest. Logged for diagnostics.
                _logger.LogWarning(ex,
                    "PR status check failed for story {StoryId} ({Url})",
                    story.Id, story.PullRequestUrl);
                continue;
            }

            if (status is not { Merged: true }) continue;
            if (story.Status == UserStoryStatus.Done) continue;

            var oldStatus = story.Status.ToString();
            ApplyDoneTransition(story);
            updates.Add(new UpdatedStoryDto(
                StoryId: story.Id,
                StoryTitle: story.Title,
                OldStatus: oldStatus,
                NewStatus: story.Status.ToString(),
                PrMerged: true,
                PrState: status.State));
            notifyDtos.Add(story.ToDto());
        }

        if (updates.Count > 0)
            await _db.SaveChangesAsync(ct);

        // Notifier fan-out happens after the row commits so reconnecting
        // clients pick up the persisted state, mirroring how
        // BacklogService.UpdateStoryStatusAsync sequences its push.
        foreach (var dto in notifyDtos)
            await _notifier.StoryUpdatedAsync(repositoryId, dto, ct);

        return new SyncPullRequestStatusesResultDto(
            Success: true,
            RepositoryId: repositoryId,
            UpdatedCount: updates.Count,
            UpdatedStories: updates);
    }

    private void ApplyDoneTransition(UserStory story)
    {
        story.SetStatus(UserStoryStatus.Done);
        _db.AppendStoryEvent(
            story,
            story.FeatureId,
            story.Feature.EpicId,
            story.Feature.Epic.RepositoryId,
            StoryEventKind.Done);
    }

    private async Task<PullRequestStatusInfo?> ResolveStatusAsync(
        string prUrl,
        string callerUserId,
        CancellationToken ct)
    {
        var parsed = PullRequestUrlParser.TryParse(prUrl);
        return parsed switch
        {
            ParsedGitHubPullRequestUrl gh => await ResolveGitHubAsync(gh, callerUserId, ct),
            ParsedAzureDevOpsPullRequestUrl ado => await ResolveAzureDevOpsAsync(ado, callerUserId, ct),
            _ => null
        };
    }

    private async Task<PullRequestStatusInfo?> ResolveGitHubAsync(
        ParsedGitHubPullRequestUrl pr, string callerUserId, CancellationToken ct)
    {
        var token = await GetLinkedProviderTokenAsync(callerUserId, LinkedProviderKind.GitHub, ct);
        if (token is null) return null;
        return await _gitHubClient.GetPullRequestStatusAsync(
            pr.Owner, pr.Repo, pr.Number, token, ct);
    }

    private async Task<PullRequestStatusInfo?> ResolveAzureDevOpsAsync(
        ParsedAzureDevOpsPullRequestUrl pr, string callerUserId, CancellationToken ct)
    {
        var token = await GetLinkedProviderTokenAsync(callerUserId, LinkedProviderKind.AzureDevOps, ct);
        if (token is null) return null;
        return await _azureDevOpsClient.GetPullRequestStatusAsync(
            pr.Organization, pr.Project, pr.Repository, pr.Number, token, ct);
    }

    private async Task<string?> GetLinkedProviderTokenAsync(
        string userId, LinkedProviderKind kind, CancellationToken ct)
    {
        var linked = await _db.LinkedProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OwnerUserId == userId && p.Provider == kind, ct);
        if (linked is null) return null;
        return await _secretStore.ResolveAsync(linked.AccessToken, ct) ?? linked.AccessToken;
    }
}
