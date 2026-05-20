// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.Services;

namespace Andy.Issues.Application.Mapping;

// SP.0.4 (andy-issues#180 / conductor#1632) — mapping helpers shared
// between the controller path (sync return after a manual GET) and the
// outbox-event path (background completion). The triage-state
// derivation lives here so both callers see the same rules:
//
//   • RefinedAt == null              → NotTriaged
//   • An in-flight run is registered → Triaging
//   • Live ContentHash ≠ snapshot    → Obsolete
//   • Otherwise                      → Triaged
//
// "An in-flight run" is signalled by the in-memory idempotency tracker
// the service maintains (RefineVersion > 0 alone is not enough — that
// covers a completed past run too). The controller passes `triaging:
// false` for the GET path; the orchestrator passes `triaging: true`
// when emitting the "queued" wire payload (currently unused — we
// always emit completion, not queued).
public static class StoryRefinementMapping
{
    public static StoryTriageStateDto DeriveTriageState(this UserStory story, bool triaging = false)
    {
        if (triaging) return StoryTriageStateDto.Triaging.Instance;
        if (story.RefinedAt is null) return StoryTriageStateDto.NotTriaged.Instance;

        var liveHash = story.ContentHash ?? StoryContentHasher.Compute(story);
        // Drift detection: when the persisted ContentHash drifts from
        // the snapshot we captured at refine time, surface as Obsolete
        // so the Conductor RefinementPanel prompts a re-refine.
        if (!string.IsNullOrEmpty(story.StoryContentHashAtTriage) &&
            !string.Equals(liveHash, story.StoryContentHashAtTriage, StringComparison.Ordinal))
        {
            return new StoryTriageStateDto.Obsolete(story.RefineVersion, story.RefinedAt.Value);
        }

        return new StoryTriageStateDto.Triaged(story.RefineVersion, story.RefinedAt.Value);
    }

    public static StoryRefinementDto? ToRefinementDto(this UserStory story)
    {
        if (story.RefinedAt is null || story.RefinedBy is null) return null;

        return new StoryRefinementDto(
            RefinedDescription: story.RefinedDescription,
            AcceptanceCriteria: story.AcceptanceCriteriaList.ToList(),
            Risks: story.Risks.ToList(),
            TestPlan: story.TestPlan.ToList(),
            Classification: new StoryClassificationDto(
                Priority: ToWire(story.Priority ?? StoryPriority.P2),
                Complexity: ToWire(story.Complexity ?? StoryComplexity.Medium),
                Risk: ToWire(story.Risk ?? StoryRisk.Medium),
                SuggestedApproach: story.SuggestedApproach),
            RefineVersion: story.RefineVersion,
            RefinedAt: story.RefinedAt.Value,
            RefinedBy: story.RefinedBy);
    }

    public static StoryPriorityWire ToWire(StoryPriority p) => p switch
    {
        StoryPriority.P0 => StoryPriorityWire.p0,
        StoryPriority.P1 => StoryPriorityWire.p1,
        StoryPriority.P2 => StoryPriorityWire.p2,
        StoryPriority.P3 => StoryPriorityWire.p3,
        _ => StoryPriorityWire.p2
    };

    public static StoryComplexityWire ToWire(StoryComplexity c) => c switch
    {
        StoryComplexity.Trivial => StoryComplexityWire.trivial,
        StoryComplexity.Small => StoryComplexityWire.small,
        StoryComplexity.Medium => StoryComplexityWire.medium,
        StoryComplexity.Large => StoryComplexityWire.large,
        StoryComplexity.Xl => StoryComplexityWire.xl,
        _ => StoryComplexityWire.medium
    };

    public static StoryRiskWire ToWire(StoryRisk r) => r switch
    {
        StoryRisk.Low => StoryRiskWire.low,
        StoryRisk.Medium => StoryRiskWire.medium,
        StoryRisk.High => StoryRiskWire.high,
        _ => StoryRiskWire.medium
    };
}
