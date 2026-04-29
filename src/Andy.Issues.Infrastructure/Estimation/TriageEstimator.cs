// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.ValueTypes;

namespace Andy.Issues.Infrastructure.Estimation;

// Z7 — cold-start estimator. Loads `defaults.json` once at
// construction and serves an EstimateSlot per (template, severity).
// Severity multiplies cost AND time.
//
// `tenantId` is accepted for forward-compatibility (the learned-model
// path will key per `{tenant, template}` per AI6 reconciliation), but
// is ignored today — defaults are global.
public sealed class TriageEstimator : ITriageEstimator
{
    private readonly EstimatorDefaults _defaults;

    public TriageEstimator()
    {
        _defaults = LoadEmbeddedDefaults();
    }

    // Constructor overload for tests that want to inject a known set of
    // baselines without touching the embedded resource.
    internal TriageEstimator(EstimatorDefaults defaults)
    {
        _defaults = defaults;
    }

    public EstimateSlot Estimate(string tenantId, TriageTemplateId templateId, TriageSeverity severity)
    {
        if (!_defaults.Templates.TryGetValue(templateId.ToString(), out var baseline))
        {
            // Unknown template — return an empty slot rather than
            // guess. This is reachable only if the enum gains a new
            // member without an updated defaults.json (CI gate
            // catches this in TriageEstimatorTests).
            return new EstimateSlot(EstimatedBy: "cold-start:unknown-template", At: DateTimeOffset.UtcNow);
        }

        var multiplier = _defaults.SeverityMultipliers
            .TryGetValue(severity.ToString(), out var m) ? m : 1.0;

        return new EstimateSlot(
            CostP50: baseline.CostP50 * multiplier,
            CostP90: baseline.CostP90 * multiplier,
            TimeP50: baseline.TimeP50 * multiplier,
            TimeP90: baseline.TimeP90 * multiplier,
            EstimatedBy: "cold-start",
            At: DateTimeOffset.UtcNow);
    }

    private static EstimatorDefaults LoadEmbeddedDefaults()
    {
        var assembly = typeof(TriageEstimator).Assembly;
        const string resourceName = "Andy.Issues.Infrastructure.Estimation.defaults.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Missing embedded resource '{resourceName}'. Check Andy.Issues.Infrastructure.csproj <EmbeddedResource> entry.");
        var defaults = JsonSerializer.Deserialize<EstimatorDefaults>(stream, JsonOptions)
            ?? throw new InvalidOperationException("defaults.json deserialised to null.");
        return defaults;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}

// Mirrors the JSON shape of defaults.json. Internal — the wire form
// of EstimateSlot is the production surface; this is just the seed
// loader's input.
internal sealed class EstimatorDefaults
{
    public Dictionary<string, TemplateBaseline> Templates { get; set; } = new();
    public Dictionary<string, double> SeverityMultipliers { get; set; } = new();
}

internal sealed class TemplateBaseline
{
    public double CostP50 { get; set; }
    public double CostP90 { get; set; }
    public double TimeP50 { get; set; }
    public double TimeP90 { get; set; }
}
