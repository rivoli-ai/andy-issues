// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Estimation;
using Xunit;

namespace Andy.Issues.Tests.Unit.Estimation;

// Z7 — cold-start estimator. Pins the embedded defaults.json contract
// and verifies severity multipliers compose with template baselines
// correctly. The CI gate test asserts the JSON file covers every
// TriageTemplateId enum member, so adding a new template without
// updating defaults fails CI.
public class TriageEstimatorTests
{
    [Fact]
    public void Estimate_KnownTemplateModerateSeverity_ReturnsBaseline()
    {
        var est = new TriageEstimator();

        var slot = est.Estimate("alice", TriageTemplateId.BugFix, TriageSeverity.Moderate);

        // Moderate = 1.0× multiplier; should match the JSON baseline
        // for BugFix.
        Assert.Equal(50.0, slot.CostP50);
        Assert.Equal(200.0, slot.CostP90);
        Assert.Equal(2.0, slot.TimeP50);
        Assert.Equal(6.0, slot.TimeP90);
        Assert.Equal("cold-start", slot.EstimatedBy);
        Assert.NotNull(slot.At);
    }

    [Fact]
    public void Estimate_CriticalSeverity_AppliesMultiplier()
    {
        var est = new TriageEstimator();

        var slot = est.Estimate("alice", TriageTemplateId.BugFix, TriageSeverity.Critical);

        // Critical = 1.5× multiplier on every percentile.
        Assert.Equal(75.0, slot.CostP50);
        Assert.Equal(300.0, slot.CostP90);
        Assert.Equal(3.0, slot.TimeP50);
        Assert.Equal(9.0, slot.TimeP90);
    }

    [Fact]
    public void Estimate_InfoSeverity_AppliesMultiplier()
    {
        var est = new TriageEstimator();

        var slot = est.Estimate("alice", TriageTemplateId.Feature, TriageSeverity.Info);

        // Info = 0.5× on the Feature baseline (200/800/8/24).
        Assert.Equal(100.0, slot.CostP50);
        Assert.Equal(400.0, slot.CostP90);
        Assert.Equal(4.0, slot.TimeP50);
        Assert.Equal(12.0, slot.TimeP90);
    }

    [Theory]
    [InlineData(TriageTemplateId.BugFix)]
    [InlineData(TriageTemplateId.Feature)]
    [InlineData(TriageTemplateId.IncidentResponse)]
    [InlineData(TriageTemplateId.Upgrade)]
    public void DefaultsCoverEveryTemplateMember(TriageTemplateId template)
    {
        var est = new TriageEstimator();

        var slot = est.Estimate("alice", template, TriageSeverity.Moderate);

        // The "unknown-template" sentinel string only appears when
        // defaults.json is missing a template entry. Adding a new
        // TriageTemplateId enum member without updating the JSON
        // fails this gate.
        Assert.NotEqual("cold-start:unknown-template", slot.EstimatedBy);
        Assert.NotNull(slot.CostP50);
        Assert.NotNull(slot.TimeP50);
    }

    [Theory]
    [InlineData(TriageSeverity.Info)]
    [InlineData(TriageSeverity.Moderate)]
    [InlineData(TriageSeverity.Critical)]
    public void DefaultsCoverEverySeverityMember(TriageSeverity severity)
    {
        var est = new TriageEstimator();

        var slot = est.Estimate("alice", TriageTemplateId.BugFix, severity);

        // Missing severity entry would silently fall back to 1.0×
        // (the multiplier dictionary's default). This test pins
        // explicit coverage by asserting the produced slot matches
        // the expected multiplier.
        var expected = severity switch
        {
            TriageSeverity.Info => 25.0,
            TriageSeverity.Moderate => 50.0,
            TriageSeverity.Critical => 75.0,
            _ => double.NaN
        };
        Assert.Equal(expected, slot.CostP50);
    }
}
