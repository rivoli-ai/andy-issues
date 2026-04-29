// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.ValueTypes;

namespace Andy.Issues.Application.Interfaces;

// Z7 — cold-start estimator. Returns an EstimateSlot for a triage
// classification using per-template seed defaults modulated by
// severity. Today this is the only path; once andy-tasks AI6 starts
// emitting `estimate_training_sample_recorded` events and N=10
// completions accumulate per `{tenant, template}`, the learned model
// kicks in (out of scope for this story).
//
// The estimator is called from IssueService.CompleteTriageAsync only
// when the agent left InitialEstimate empty — agent-supplied
// estimates are preserved.
public interface ITriageEstimator
{
    EstimateSlot Estimate(string tenantId, TriageTemplateId templateId, TriageSeverity severity);
}
