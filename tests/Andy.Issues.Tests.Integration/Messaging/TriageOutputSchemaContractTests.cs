// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.ValueTypes;
using Xunit;

namespace Andy.Issues.Tests.Integration.Messaging;

// Z12 — schema contract tests for the `triaged` event payload.
//
// `schemas/triage-output.v1.json` is the canonical contract that
// downstream consumers (notably andy-tasks AA4 → Goal materialisation)
// pin against. These tests freeze the wire shape: any drift between
// the runtime JSON produced by `EventJson.Options` and the schema
// file fails the suite.
//
// Validation is hand-rolled rather than via JsonSchema.Net so this
// test file stays dependency-free; the assertions cover the invariants
// the schema actually expresses (required fields, snake_case keys,
// enum values, no unknown properties).
public class TriageOutputSchemaContractTests
{
    private static readonly string SchemaPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "schemas", "triage-output.v1.json");

    [Fact]
    public void SchemaFile_Exists_AtKnownPath()
    {
        Assert.True(File.Exists(SchemaPath),
            $"Z3 schema file missing at {SchemaPath}. Z12 contract tests pin to this file.");
    }

    [Fact]
    public void TriageOutput_FullyPopulated_RoundTripsViaEventJson()
    {
        var output = SampleOutput();

        var bytes = JsonSerializer.SerializeToUtf8Bytes(output, EventJson.Options);
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;

        // Required fields per schema $schema.required
        AssertHas(root, "template_id");
        AssertHas(root, "severity");
        AssertHas(root, "rationale");
        AssertHas(root, "inputs_docs_refs");
        AssertHas(root, "initial_estimate");
        AssertHas(root, "schema_version");

        // Enum bounds
        Assert.Contains(
            root.GetProperty("template_id").GetString(),
            new[] { "bug_fix", "feature", "incident_response", "upgrade" });
        Assert.Contains(
            root.GetProperty("severity").GetString(),
            new[] { "info", "moderate", "critical" });

        // schema_version is the const 1
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());

        // inputs_docs_refs items have the DocsRef shape
        foreach (var item in root.GetProperty("inputs_docs_refs").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Object, item.ValueKind);
            AssertHas(item, "document_id");
            AssertHas(item, "link_id");
            // Both are well-formed UUIDs
            Assert.True(Guid.TryParse(item.GetProperty("document_id").GetString(), out _));
            Assert.True(Guid.TryParse(item.GetProperty("link_id").GetString(), out _));
        }

        // initial_estimate has the EstimateSlot shape (additionalProperties: false)
        var slot = root.GetProperty("initial_estimate");
        Assert.Equal(JsonValueKind.Object, slot.ValueKind);
        var allowedSlotKeys = new HashSet<string>
        {
            "cost_p50", "cost_p90", "time_p50", "time_p90", "estimated_by", "at"
        };
        foreach (var p in slot.EnumerateObject())
            Assert.Contains(p.Name, allowedSlotKeys);
    }

    [Fact]
    public void TriageOutput_OnlyExpectedTopLevelKeys()
    {
        var output = SampleOutput();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(output, EventJson.Options);
        using var doc = JsonDocument.Parse(bytes);

        var allowed = new HashSet<string>
        {
            "template_id", "severity", "suggested_repo", "rationale",
            "inputs_docs_refs", "initial_estimate", "schema_version"
        };
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            Assert.True(allowed.Contains(prop.Name),
                $"Unexpected top-level property '{prop.Name}' would violate schema additionalProperties: false");
        }
    }

    [Fact]
    public void IssueEventPayload_TriageOutputEmbedsCanonicalShape()
    {
        // The `triaged` outbox subject carries an IssueEventPayload whose
        // TriageOutput field is the schema-typed payload. Verify the
        // embedded shape survives the envelope serialization.
        var payload = new IssueEventPayload(
            IssueId: Guid.NewGuid(),
            RepositoryId: Guid.NewGuid(),
            OwnerUserId: "alice",
            Title: "broken thing",
            TriageState: TriageState.Triaged.ToString(),
            TriagedBy: "alice",
            TriagedAt: DateTimeOffset.UtcNow,
            TriageOutput: SampleOutput());

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, EventJson.Options);
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;

        // Envelope itself is snake_case
        AssertHas(root, "issue_id");
        AssertHas(root, "triage_state");
        AssertHas(root, "triage_output");

        // Embedded triage_output preserves the v1 contract
        var embedded = root.GetProperty("triage_output");
        AssertHas(embedded, "template_id");
        AssertHas(embedded, "severity");
        AssertHas(embedded, "rationale");
        AssertHas(embedded, "schema_version");
        Assert.Equal(1, embedded.GetProperty("schema_version").GetInt32());
    }

    [Fact]
    public void TriageOutput_SuggestedRepoOmitted_WhenNull()
    {
        var output = SampleOutput() with { SuggestedRepo = null };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(output, EventJson.Options);
        using var doc = JsonDocument.Parse(bytes);

        // Schema says suggested_repo is `["string", "null"]`. EventJson
        // happens to omit nulls (DefaultIgnoreCondition); both shapes
        // satisfy the schema. Test pins the actual current behavior so
        // a serializer config drift fails the suite.
        Assert.False(doc.RootElement.TryGetProperty("suggested_repo", out _));
    }

    private static TriageOutput SampleOutput() => new(
        TemplateId: TriageTemplateId.BugFix,
        Severity: TriageSeverity.Critical,
        SuggestedRepo: "acme/widget",
        Rationale: "duplicate keypress causes double-submit",
        InputsDocsRefs: new[]
        {
            new DocsRef(Guid.NewGuid(), Guid.NewGuid()),
            new DocsRef(Guid.NewGuid(), Guid.NewGuid()),
        },
        InitialEstimate: new EstimateSlot(
            CostP50: 1500.0,
            CostP90: 4000.0,
            TimeP50: 2.0,
            TimeP90: 5.0,
            EstimatedBy: "z7-cold-start",
            At: DateTimeOffset.UtcNow));

    private static void AssertHas(JsonElement element, string property)
    {
        Assert.True(element.TryGetProperty(property, out _),
            $"Required property '{property}' missing from {element.GetRawText()[..Math.Min(120, element.GetRawText().Length)]}…");
    }
}
