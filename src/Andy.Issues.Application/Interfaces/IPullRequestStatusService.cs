// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;

namespace Andy.Issues.Application.Interfaces;

public enum HeadBranchOutcome
{
    Ok = 0,
    // URL didn't match a recognised GitHub or Azure DevOps PR shape.
    BadUrl = 1,
    // URL parsed but the upstream API said the PR doesn't exist, or
    // the caller has no LinkedProvider credentials to query it.
    NotFound = 2
}

public interface IPullRequestStatusService
{
    // #89 — single-story check. Null = story not found / not visible
    // to caller. The DTO carries hasPr=false (with other fields null)
    // when the story has no PullRequestUrl.
    Task<StoryPullRequestStatusDto?> CheckStoryAsync(
        Guid storyId,
        string userId,
        CancellationToken ct = default);

    // #88 — repository-wide sync. Null = repo not found / not visible.
    Task<SyncPullRequestStatusesResultDto?> SyncRepositoryAsync(
        Guid repositoryId,
        string userId,
        CancellationToken ct = default);

    // #90 — resolve a PR URL to its head branch. Reuses the same
    // upstream fetch as the status endpoints (head branch is included
    // in that response) so a PR-status check and a head-branch lookup
    // cost the same.
    Task<(HeadBranchOutcome Outcome, string? Branch)> GetHeadBranchByUrlAsync(
        string? url,
        string callerUserId,
        CancellationToken ct = default);
}
