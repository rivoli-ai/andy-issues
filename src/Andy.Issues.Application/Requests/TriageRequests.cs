// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.ValueTypes;

namespace Andy.Issues.Application.Requests;

// Z5 — human-edit body for PATCH /api/triage/{id}/output. Pairs a
// new TriageOutput with an optional short summary that Conductor's
// AB5 version timeline renders.
public record EditTriageOutputRequest(
    TriageOutput Output,
    string? DiffSummary);

// Z5 — body for POST /api/triage/{id}/revert.
public record RevertTriageRequest(
    Guid TargetRevisionId);
