// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Infrastructure.Triage;

// SP.0.4 (andy-issues#180) — Wave-1 stub implementation of
// IStoryTriageAgent. Produces deterministic, plausible-looking
// classification + scaffolded refinement output so the Conductor
// RefinementPanel renders end-to-end without an external LLM.
//
// A real LLM-backed adapter lands in the SP.0.13 follow-up; this stub
// is wired into DI by default and the future adapter will be selected
// per-workspace via a feature flag. The contract is exercised by the
// integration tests so the swap is mechanical.
//
// Heuristics (intentionally simple — they exist to make the UI feel
// alive, not to ship a real triage model):
//   • Priority   — derived from labels: any label matching "p0"/
//                  "critical"/"urgent" → P0; "p1"/"high" → P1; "p3"/
//                  "low"/"minor" → P3; otherwise → P2.
//   • Complexity — derived from body length: <200 chars → Trivial,
//                  <800 → Small, <2000 → Medium, <5000 → Large, else
//                  XL.
//   • Risk       — fixed Medium (matches the spec hint).
//   • Suggested approach — a one-liner re-stating the title.
//
// The acceptance criteria, risks, and test-plan lists are short
// scaffolds the user can tighten — never empty, so the UI never
// renders an empty section.
public sealed class EchoStoryTriageAgent : IStoryTriageAgent
{
    public Task<StoryTriageAgentOutput> RefineAsync(
        StoryTriageAgentInput input, CancellationToken ct = default)
    {
        var priority = DerivePriority(input.Labels);
        var complexity = DeriveComplexity(input.Description, input.AcceptanceCriteria);
        const StoryRisk risk = StoryRisk.Medium;

        var titleSnippet = string.IsNullOrWhiteSpace(input.Title)
            ? "this work item"
            : input.Title.Trim();

        var refinedDescription = ComposeDescription(input);

        var acceptanceCriteria = new List<string>
        {
            $"Given the implementation lands behind a feature flag, when a user opens {titleSnippet}, then the new behaviour is observable.",
            $"Given the change is shipped, when the relevant tests run in CI, then they pass without manual intervention.",
            $"Given regressions appear, when the feature flag is disabled, then the previous behaviour is restored."
        };

        var risks = new List<string>
        {
            $"Hidden coupling: changes to {titleSnippet} may surface in unrelated panels.",
            "Observability gap: no metric currently covers the new code path.",
            "Migration risk: persisted state may diverge if the rollout is partial."
        };

        var testPlan = new List<string>
        {
            "Unit tests cover the new state transitions and validation rules.",
            "Integration tests exercise the happy path end-to-end through the service.",
            "Manual smoke test: open the affected panel and walk through the user journey."
        };

        var suggestedApproach = $"Implement {titleSnippet} behind a feature flag, ship tests alongside the change, and roll out incrementally.";

        return Task.FromResult(new StoryTriageAgentOutput(
            RefinedDescription: refinedDescription,
            AcceptanceCriteria: acceptanceCriteria,
            Risks: risks,
            TestPlan: testPlan,
            Priority: priority,
            Complexity: complexity,
            Risk: risk,
            SuggestedApproach: suggestedApproach));
    }

    private static StoryPriority DerivePriority(IReadOnlyList<string> labels)
    {
        foreach (var raw in labels)
        {
            var l = raw.Trim().ToLowerInvariant();
            if (l is "p0" or "critical" or "urgent" or "priority:p0-critical")
                return StoryPriority.P0;
            if (l is "p1" or "high" or "priority:p1-high")
                return StoryPriority.P1;
            if (l is "p3" or "low" or "minor" or "priority:p3-low")
                return StoryPriority.P3;
        }
        return StoryPriority.P2;
    }

    private static StoryComplexity DeriveComplexity(string? description, string? acceptanceCriteria)
    {
        var length = (description?.Length ?? 0) + (acceptanceCriteria?.Length ?? 0);
        return length switch
        {
            < 200 => StoryComplexity.Trivial,
            < 800 => StoryComplexity.Small,
            < 2000 => StoryComplexity.Medium,
            < 5000 => StoryComplexity.Large,
            _ => StoryComplexity.Xl
        };
    }

    private static string ComposeDescription(StoryTriageAgentInput input)
    {
        // Echo the existing description with a "Refined by" prefix so the
        // wire shape is non-trivially different from the input — this
        // matters for Conductor smoke tests that assert the refinement
        // actually changed the visible body.
        var basis = string.IsNullOrWhiteSpace(input.Description)
            ? input.Title
            : input.Description!.Trim();

        var instructionsLine = string.IsNullOrWhiteSpace(input.Instructions)
            ? string.Empty
            : $"\n\nApplied instructions: {input.Instructions!.Trim()}";

        return $"[Refined by {input.AgentId}] {basis}{instructionsLine}";
    }
}
