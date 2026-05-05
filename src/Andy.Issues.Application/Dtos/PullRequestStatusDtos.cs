// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

// Wire shape for #89 — single-story PR check.
public sealed record StoryPullRequestStatusDto(
    Guid StoryId,
    bool HasPr,
    string? PrUrl,
    string? PrState,
    bool? PrMerged,
    DateTimeOffset? PrMergedAt,
    string StoryStatus,
    bool StatusUpdated);

// Wire shape for #88 — repository-wide batch sync.
public sealed record SyncPullRequestStatusesResultDto(
    bool Success,
    Guid RepositoryId,
    int UpdatedCount,
    IReadOnlyList<UpdatedStoryDto> UpdatedStories);

public sealed record UpdatedStoryDto(
    Guid StoryId,
    string StoryTitle,
    string OldStatus,
    string NewStatus,
    bool PrMerged,
    string PrState);
