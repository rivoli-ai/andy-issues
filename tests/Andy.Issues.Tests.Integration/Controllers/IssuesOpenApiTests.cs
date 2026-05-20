// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

// #187 — schema contract test for the unified `GET /api/issues`
// endpoint. Pins the response code set + the registered schemas so
// the conductor cockpit consumers (AF2 + AF3) see a stable wire
// contract through any future schema regeneration.
public class IssuesOpenApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public IssuesOpenApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private OpenApiDocument GetSchema()
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISwaggerProvider>();
        return provider.GetSwagger("v1");
    }

    [Fact]
    public void Issues_List_DeclaresExpectedResponseCodes()
    {
        var schema = GetSchema();
        Assert.True(schema.Paths.TryGetValue("/api/issues", out var pathItem),
            "OpenAPI schema does not contain '/api/issues'.");

        Assert.True(pathItem.Operations.TryGetValue(OperationType.Get, out var op),
            "OpenAPI schema does not contain GET on '/api/issues'.");

        var actual = op.Responses.Keys.OrderBy(k => k).ToArray();
        Assert.Equal(new[] { "200", "400", "401" }.OrderBy(k => k).ToArray(), actual);
    }

    [Fact]
    public void Issues_List_DeclaresAllQueryParameters()
    {
        // The cockpit consumers (AF2 + AF3) drive their typed
        // protocol method off this schema. Missing a parameter here
        // means the generated client silently drops the filter.
        var schema = GetSchema();
        var op = schema.Paths["/api/issues"].Operations[OperationType.Get];
        var names = op.Parameters.Select(p => p.Name).ToHashSet();
        Assert.Contains("state", names);
        Assert.Contains("assignee", names);
        Assert.Contains("repository", names);
        Assert.Contains("limit", names);
        Assert.Contains("cursor", names);
    }

    [Fact]
    public void IssueListResponse_IsRegisteredAsSchema()
    {
        var schema = GetSchema();
        Assert.True(schema.Components.Schemas.ContainsKey("IssueListResponse"),
            "IssueListResponse schema not registered — generated clients would see an inline anonymous object.");
    }

    [Fact]
    public void IssueSummary_IsRegisteredAsSchema()
    {
        var schema = GetSchema();
        Assert.True(schema.Components.Schemas.ContainsKey("IssueSummary"),
            "IssueSummary schema not registered.");
    }

    [Fact]
    public void IssueListErrorResponse_IsRegisteredAsSchema()
    {
        var schema = GetSchema();
        Assert.True(schema.Components.Schemas.ContainsKey("IssueListErrorResponse"),
            "IssueListErrorResponse schema not registered — generated clients would see an inline anonymous object on 400 responses.");
    }
}
