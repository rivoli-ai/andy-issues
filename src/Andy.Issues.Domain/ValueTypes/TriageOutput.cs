// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Domain.ValueTypes;

// Canonical triage output (Z3). Stored as a JSON column on the Issue
// entity and emitted as the full payload of the `triaged` event in Z4.
// Fields:
//   TemplateId       — classification picked from the four WorkflowTemplate
//                      seeds shared with andy-tasks (AA2).
//   Severity         — categorical, drives retention + gating.
//   SuggestedRepo    — repo slug ("owner/repo"); a string rather than a
//                      Repository GUID because triage may propose a repo
//                      not yet known to the system.
//   Rationale        — short human-readable justification. Required.
//   InputsDocsRefs   — echoes Z8 attachments. Empty when no inputs are
//                      attached. Inherited as TaskNode.Inputs[] when
//                      andy-tasks creates the Goal.
//   InitialEstimate  — Z7 estimator output. Empty EstimateSlot until Z7
//                      lands.
//
// Schema is frozen at v1. Future shape changes (e.g. adding `version`
// to DocsRef) bump to v2 and ship a separate `triage-output.v2.json`.
public sealed record TriageOutput(
    TriageTemplateId TemplateId,
    TriageSeverity Severity,
    string? SuggestedRepo,
    string Rationale,
    IReadOnlyList<DocsRef> InputsDocsRefs,
    EstimateSlot InitialEstimate)
{
    public const int SchemaVersion = 1;

    // Snake_case alias surfaced by EventJson.Options (matches
    // StoryEventPayload's convention). Read-only, derived from the
    // const above.
    public int Schema_Version => SchemaVersion;
}
