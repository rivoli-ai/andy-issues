// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;

namespace Andy.Issues.Application.Interfaces;

// SP.0.4 (andy-issues#180 / conductor#1632) — orchestrator for the
// long-running story refinement flow. Controller invokes RefineAsync;
// the implementation:
//
//   1. Validates the story exists + the caller has read access (the
//      "stories:refine" permission shares the read scope today).
//   2. Allocates a refineRunId + bumps RefineVersion.
//   3. Returns 202 (RefineQueued) immediately while the agent call
//      runs on a background task off the request scope.
//   4. On completion, persists the classification + acceptance criteria
//      / risks / test plan onto the UserStory row and emits a
//      `story.{id}.triaged` outbox row.
//
// Idempotency: a second call within IdempotencyWindow on the same
// (storyId, agentId) pair while the first call is still in-flight
// returns the original (refineRunId, refineVersion) pair without
// dispatching a second agent run.
public interface IStoryRefinementService
{
    Task<StoryRefineResult> RefineAsync(
        Guid storyId,
        RefineStoryRequest request,
        string userId,
        CancellationToken ct = default);
}

public enum StoryRefineOutcome
{
    Queued,
    NotFound,
    Forbidden,
    AgentUnavailable
}

public sealed record StoryRefineResult(
    StoryRefineOutcome Outcome,
    StoryRefineRunDto? Run,
    string? Error)
{
    public static StoryRefineResult Queued(StoryRefineRunDto run) =>
        new(StoryRefineOutcome.Queued, run, null);
    public static StoryRefineResult NotFound() =>
        new(StoryRefineOutcome.NotFound, null, null);
    public static StoryRefineResult Forbidden() =>
        new(StoryRefineOutcome.Forbidden, null, null);
    public static StoryRefineResult AgentUnavailable(string error) =>
        new(StoryRefineOutcome.AgentUnavailable, null, error);
}
