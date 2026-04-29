// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Requests;

// #101 — bulk delete across epic/feature/story kinds in one call.
// Empty lists are tolerated; an entirely empty body is rejected at
// the controller boundary (400) so the caller can't accidentally
// no-op without noticing.
public record BulkDeleteRequest(
    IReadOnlyList<Guid>? EpicIds = null,
    IReadOnlyList<Guid>? FeatureIds = null,
    IReadOnlyList<Guid>? StoryIds = null)
{
    public bool IsEmpty =>
        (EpicIds is null || EpicIds.Count == 0)
        && (FeatureIds is null || FeatureIds.Count == 0)
        && (StoryIds is null || StoryIds.Count == 0);
}
