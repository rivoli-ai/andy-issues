// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;

namespace Andy.Issues.Application.Interfaces;

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
}
