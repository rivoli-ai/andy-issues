// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.ValueTypes;

// Cost/time estimate slot (Z3). Shape matches rivoli-ai/andy-tasks
// Epic AI (AI1) so the `initial` slot produced by triage and the
// `planned`/`actual` slots produced during execution share one
// schema. andy-issues only writes the `initial` slot — Z7 fills it
// with a learned per-tenant model.
//
// All fields are nullable because Z7 hasn't shipped yet. A Z3
// `TriageOutput` with `InitialEstimate = new EstimateSlot()` is
// well-formed; Z7 will populate the percentile fields.
public sealed record EstimateSlot(
    double? CostP50 = null,
    double? CostP90 = null,
    double? TimeP50 = null,
    double? TimeP90 = null,
    string? EstimatedBy = null,
    DateTimeOffset? At = null);
