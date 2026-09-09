// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.ValueTypes;

namespace Andy.Issues.Application.Interfaces;

// Server-side estimates; implementations fall back to per-template defaults.
public interface ITriageEstimator
{
    EstimateSlot Estimate(string tenantId, TriageTemplateId templateId, TriageSeverity severity);
    Task<EstimateSlot> EstimateAsync(string tenantId, TriageTemplateId templateId, TriageSeverity severity,
        string? repository, string? description, CancellationToken ct = default) =>
        Task.FromResult(Estimate(tenantId, templateId, severity));
}
