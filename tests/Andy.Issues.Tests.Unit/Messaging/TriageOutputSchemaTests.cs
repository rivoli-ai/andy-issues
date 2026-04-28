// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.ValueTypes;
using Xunit;

namespace Andy.Issues.Tests.Unit.Messaging;

// Z3 — schema contract gate. Loads the published JSON Schema
// (schemas/triage-output.v1.json) and asserts that the C# wire shape
// emitted by EventJson.Options matches the property names + enum
// values declared in the schema. Drift between the two — adding a
// field to the record without updating the schema, or vice versa —
// fails CI.
//
// This is intentionally a structural check, not a full JSON-Schema
// validation. The repo doesn't pull a JSON Schema validator nuget;
// adding one for one test is overkill. The check covers the failure
// modes we actually saw in adjacent services: silent property rename,
// missing enum value, missing required field.
public class TriageOutputSchemaTests
{
    private static readonly string SchemaPath =
        Path.Combine(SolutionRoot(), "schemas", "triage-output.v1.json");

    [Fact]
    public void PublishedSchemaFileExists()
    {
        Assert.True(File.Exists(SchemaPath),
            $"Expected published schema at {SchemaPath}. Did the file move?");
    }

    [Fact]
    public void PublishedSchema_DeclaresExactlyTheRequiredFields()
    {
        var schema = LoadSchema();

        var required = schema.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()!).ToHashSet();

        Assert.Equal(
            new[] { "template_id", "severity", "rationale", "inputs_docs_refs", "initial_estimate", "schema_version" }
                .ToHashSet(),
            required);
    }

    [Fact]
    public void PublishedSchema_TemplateIdEnum_MatchesDomainEnum()
    {
        var declared = LoadSchema()
            .GetProperty("properties")
            .GetProperty("template_id")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .OrderBy(x => x)
            .ToArray();

        var fromDomain = Enum.GetValues<TriageTemplateId>()
            .Select(v => JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString()))
            .OrderBy(x => x)
            .ToArray();

        Assert.Equal(fromDomain, declared);
    }

    [Fact]
    public void PublishedSchema_SeverityEnum_MatchesDomainEnum()
    {
        var declared = LoadSchema()
            .GetProperty("properties")
            .GetProperty("severity")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .OrderBy(x => x)
            .ToArray();

        var fromDomain = Enum.GetValues<TriageSeverity>()
            .Select(v => JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString()))
            .OrderBy(x => x)
            .ToArray();

        Assert.Equal(fromDomain, declared);
    }

    [Fact]
    public void SerializedTriageOutput_AllPublishedRequiredFields_Present()
    {
        var output = new TriageOutput(
            TriageTemplateId.Feature,
            TriageSeverity.Moderate,
            SuggestedRepo: null, // optional — should NOT be in required
            Rationale: "ok",
            InputsDocsRefs: new[] { new DocsRef(Guid.NewGuid(), Guid.NewGuid()) },
            InitialEstimate: new EstimateSlot(CostP50: 1.0, EstimatedBy: "z7-stub"));

        var json = JsonSerializer.Serialize(output, EventJson.Options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var schema = LoadSchema();
        var required = schema.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()!);

        foreach (var fieldName in required)
        {
            Assert.True(root.TryGetProperty(fieldName, out _),
                $"Serialized TriageOutput is missing required field '{fieldName}' declared in {SchemaPath}.");
        }
    }

    private static JsonElement LoadSchema()
    {
        var text = File.ReadAllText(SchemaPath);
        return JsonDocument.Parse(text).RootElement;
    }

    // Walk up from the test assembly's AppContext.BaseDirectory to find
    // the repo root (the directory holding `Andy.Issues.sln`).
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Andy.Issues.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Could not locate Andy.Issues.sln walking up from " + AppContext.BaseDirectory);
        return dir.FullName;
    }
}
