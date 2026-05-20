// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Application.Interfaces;

// SP.0.4 (andy-issues#180) — thin seam over the LLM that classifies a
// user story and produces refined description / acceptance criteria /
// risks / test plan. The Wave-1 implementation is
// `EchoStoryTriageAgent`, a deterministic stub that mirrors the input
// shape so Conductor's RefinementPanel renders end-to-end without an
// external dependency.
//
// A real LLM-backed implementation lives in a follow-up — that adapter
// will plug into this same interface and a feature flag will switch
// between them per workspace.
public interface IStoryTriageAgent
{
    Task<StoryTriageAgentOutput> RefineAsync(
        StoryTriageAgentInput input,
        CancellationToken ct = default);
}

public sealed record StoryTriageAgentInput(
    Guid StoryId,
    string Title,
    string? Description,
    string? AcceptanceCriteria,
    IReadOnlyList<string> Labels,
    string? Instructions,
    string AgentId);

public sealed record StoryTriageAgentOutput(
    string? RefinedDescription,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> TestPlan,
    StoryPriority Priority,
    StoryComplexity Complexity,
    StoryRisk Risk,
    string SuggestedApproach);
