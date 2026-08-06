// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

// Z11 — schema contract test. Resolves ISwaggerProvider from the live
// container (so we exercise the actual generation pipeline, not a
// hand-rolled snapshot) and asserts that every triage operation
// declares the response codes the controller emits.
//
// This is the smallest gate that catches drift between the controller
// and the published schema. A repo-wide schema-diff CI job (per the Z11
// brief) needs a checked-in baseline first; that is out of scope here.
public class TriageOpenApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public TriageOpenApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private OpenApiDocument GetSchema()
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISwaggerProvider>();
        return provider.GetSwagger("v1");
    }

    [Theory]
    [InlineData("/api/triage", "POST", new[] { "201", "400", "401" })]
    [InlineData("/api/triage", "GET", new[] { "200", "401" })]
    [InlineData("/api/triage/{id}", "GET", new[] { "200", "401", "404" })]
    [InlineData("/api/triage/{id}/start", "POST", new[] { "200", "401", "404", "409" })]
    [InlineData("/api/triage/{id}/complete", "POST", new[] { "200", "401", "404", "409" })]
    [InlineData("/api/triage/{id}/accept", "POST", new[] { "200", "401", "404", "409" })]
    [InlineData("/api/triage/{id}/reject", "POST", new[] { "200", "401", "404", "409" })]
    // Z5 — human-edit + revisions surface
    [InlineData("/api/triage/{id}/output", "PATCH", new[] { "200", "400", "401", "404", "409" })]
    [InlineData("/api/triage/{id}/revisions", "GET", new[] { "200", "401", "404" })]
    [InlineData("/api/triage/{id}/revert", "POST", new[] { "200", "401", "404", "409" })]
    // Z8 — attachments surface
    [InlineData("/api/triage/{id}/attachments", "GET", new[] { "200", "401", "404" })]
    [InlineData("/api/triage/{id}/attachments", "POST", new[] { "200", "201", "400", "401", "404", "409" })]
    [InlineData("/api/triage/{id}/attachments/{linkId}", "DELETE", new[] { "204", "401", "404" })]
    public void TriageOperation_DeclaresExpectedResponseCodes(
        string path, string verb, string[] expectedCodes)
    {
        var schema = GetSchema();
        Assert.True(schema.Paths.TryGetValue(path, out var pathItem),
            $"OpenAPI schema does not contain path '{path}'.");

        Assert.True(pathItem!.Operations!.TryGetValue(HttpMethod.Parse(verb), out var op),
            $"OpenAPI schema does not contain {verb} on path '{path}'.");

        var actual = op!.Responses!.Keys.OrderBy(k => k).ToArray();
        Assert.Equal(expectedCodes.OrderBy(k => k).ToArray(), actual);
    }

    [Fact]
    public void TriageConflictResponse_IsRegisteredAsSchema()
    {
        var schema = GetSchema();
        Assert.True(schema.Components!.Schemas!.ContainsKey("TriageConflictResponse"),
            "TriageConflictResponse schema not registered — generated clients would see an inline anonymous object.");
    }

    [Fact]
    public void IssueDto_IsRegisteredAsSchema()
    {
        var schema = GetSchema();
        Assert.True(schema.Components!.Schemas!.ContainsKey("IssueDto"),
            "IssueDto schema not registered.");
    }
}
