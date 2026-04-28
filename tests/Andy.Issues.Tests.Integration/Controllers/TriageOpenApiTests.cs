// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
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
    [InlineData("/api/triage", OperationType.Post, new[] { "201", "400", "401" })]
    [InlineData("/api/triage", OperationType.Get, new[] { "200", "401" })]
    [InlineData("/api/triage/{id}", OperationType.Get, new[] { "200", "401", "404" })]
    [InlineData("/api/triage/{id}/start", OperationType.Post, new[] { "200", "401", "404", "409" })]
    [InlineData("/api/triage/{id}/complete", OperationType.Post, new[] { "200", "401", "404", "409" })]
    [InlineData("/api/triage/{id}/accept", OperationType.Post, new[] { "200", "401", "404", "409" })]
    [InlineData("/api/triage/{id}/reject", OperationType.Post, new[] { "200", "401", "404", "409" })]
    public void TriageOperation_DeclaresExpectedResponseCodes(
        string path, OperationType verb, string[] expectedCodes)
    {
        var schema = GetSchema();
        Assert.True(schema.Paths.TryGetValue(path, out var pathItem),
            $"OpenAPI schema does not contain path '{path}'.");

        Assert.True(pathItem.Operations.TryGetValue(verb, out var op),
            $"OpenAPI schema does not contain {verb} on path '{path}'.");

        var actual = op.Responses.Keys.OrderBy(k => k).ToArray();
        Assert.Equal(expectedCodes.OrderBy(k => k).ToArray(), actual);
    }

    [Fact]
    public void TriageConflictResponse_IsRegisteredAsSchema()
    {
        var schema = GetSchema();
        Assert.True(schema.Components.Schemas.ContainsKey("TriageConflictResponse"),
            "TriageConflictResponse schema not registered — generated clients would see an inline anonymous object.");
    }

    [Fact]
    public void IssueDto_IsRegisteredAsSchema()
    {
        var schema = GetSchema();
        Assert.True(schema.Components.Schemas.ContainsKey("IssueDto"),
            "IssueDto schema not registered.");
    }
}
