// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.ValueTypes;
using Xunit;

namespace Andy.Issues.Tests.Unit.Entities;

// Z3 — TriageOutput round-trip + entity validation. The wire shape is
// frozen at v1: see schemas/triage-output.v1.json for the contract.
// These tests pin the snake_case property names + the empty-rationale
// guard that lives on Issue.CompleteTriage.
public class TriageOutputTests
{
    [Fact]
    public void TriageOutput_RoundTripsThroughEventJson()
    {
        var output = new TriageOutput(
            TemplateId: TriageTemplateId.BugFix,
            Severity: TriageSeverity.Critical,
            SuggestedRepo: "rivoli-ai/andy-tasks",
            Rationale: "Repro confirmed; data loss on every save.",
            InputsDocsRefs: new[]
            {
                new DocsRef(Guid.NewGuid(), Guid.NewGuid())
            },
            InitialEstimate: new EstimateSlot());

        var json = JsonSerializer.Serialize(output, EventJson.Options);

        // Snake_case property names — pinned by the public schema.
        Assert.Contains("\"template_id\"", json);
        Assert.Contains("\"severity\"", json);
        Assert.Contains("\"suggested_repo\"", json);
        Assert.Contains("\"rationale\"", json);
        Assert.Contains("\"inputs_docs_refs\"", json);
        Assert.Contains("\"initial_estimate\"", json);
        Assert.Contains("\"schema_version\":1", json);

        // Enum values serialize as snake_case strings.
        Assert.Contains("\"bug_fix\"", json);
        Assert.Contains("\"critical\"", json);

        var roundTripped = JsonSerializer.Deserialize<TriageOutput>(json, EventJson.Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(output.TemplateId, roundTripped.TemplateId);
        Assert.Equal(output.Severity, roundTripped.Severity);
        Assert.Equal(output.SuggestedRepo, roundTripped.SuggestedRepo);
        Assert.Equal(output.Rationale, roundTripped.Rationale);
        Assert.Single(roundTripped.InputsDocsRefs);
    }

    [Fact]
    public void TriageOutput_NullSuggestedRepo_IsOmittedFromWire()
    {
        var output = new TriageOutput(
            TriageTemplateId.Feature, TriageSeverity.Info,
            SuggestedRepo: null,
            Rationale: "Nice to have.",
            InputsDocsRefs: Array.Empty<DocsRef>(),
            InitialEstimate: new EstimateSlot());

        var json = JsonSerializer.Serialize(output, EventJson.Options);

        // Per EventJson.Options.DefaultIgnoreCondition, nulls are
        // suppressed on the wire. Consumers that care can fill the
        // field with `null` explicitly when reading.
        Assert.DoesNotContain("\"suggested_repo\"", json);
    }

    [Fact]
    public void EstimateSlot_AllNullPercentiles_RoundTrip()
    {
        var slot = new EstimateSlot();
        var json = JsonSerializer.Serialize(slot, EventJson.Options);

        // Empty slot serialises to "{}" because every field is null
        // and DefaultIgnoreCondition.WhenWritingNull is on.
        Assert.Equal("{}", json);

        var roundTripped = JsonSerializer.Deserialize<EstimateSlot>(json, EventJson.Options);
        Assert.NotNull(roundTripped);
        Assert.Null(roundTripped.CostP50);
    }

    [Fact]
    public void Issue_CompleteTriage_StoresOutput()
    {
        var issue = new Issue { Title = "x" };
        issue.StartTriage();

        var output = MakeValidOutput();
        issue.CompleteTriage("agent-1", output);

        Assert.Equal(TriageState.Triaged, issue.TriageState);
        Assert.NotNull(issue.TriageOutput);
        Assert.Equal(TriageTemplateId.BugFix, issue.TriageOutput!.TemplateId);
    }

    [Fact]
    public void Issue_CompleteTriage_WithoutOutput_StillTransitions()
    {
        // Z1 backwards-compat — manual completes (no agent yet) leave
        // TriageOutput null.
        var issue = new Issue { Title = "x" };
        issue.StartTriage();
        issue.CompleteTriage("alice");

        Assert.Equal(TriageState.Triaged, issue.TriageState);
        Assert.Null(issue.TriageOutput);
    }

    [Fact]
    public void Issue_CompleteTriage_EmptyRationale_Throws()
    {
        var issue = new Issue { Title = "x" };
        issue.StartTriage();

        var bad = new TriageOutput(
            TriageTemplateId.BugFix, TriageSeverity.Info,
            SuggestedRepo: null, Rationale: "   ",
            InputsDocsRefs: Array.Empty<DocsRef>(),
            InitialEstimate: new EstimateSlot());

        Assert.Throws<ArgumentException>(() => issue.CompleteTriage("agent-1", bad));
        // Failed validation must leave the issue in Triaging — the
        // entity rejects the call before mutating state.
        Assert.Equal(TriageState.Triaging, issue.TriageState);
        Assert.Null(issue.TriageOutput);
    }

    private static TriageOutput MakeValidOutput() => new(
        TriageTemplateId.BugFix,
        TriageSeverity.Critical,
        "rivoli-ai/andy-tasks",
        "Repro confirmed.",
        Array.Empty<DocsRef>(),
        new EstimateSlot());
}
