// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

/// <summary>
/// Integration coverage for <c>POST /api/backlog/suggest</c>. Focuses
/// on the outcome-to-status mapping in <c>BacklogController</c> and
/// the service-layer guard logic that runs before the LLM HTTP call.
/// The LLM HTTP call itself is exercised by unit tests (a real HTTP
/// stub is harder to wire through <c>IHttpClientFactory</c> here
/// without a fake; not worth the infrastructure for this PR).
/// </summary>
public class BacklogControllerSuggestTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BacklogControllerSuggestTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Suggest_UnknownField_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/backlog/suggest",
            new SuggestContentRequest(
                Field: "summary",
                ItemType: "story",
                Title: "Login flow",
                CurrentContent: null,
                RepositoryId: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Suggest_AcceptanceCriteriaOnEpic_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/backlog/suggest",
            new SuggestContentRequest(
                Field: "acceptanceCriteria",
                ItemType: "epic",
                Title: "Authentication",
                CurrentContent: null,
                RepositoryId: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Suggest_UnknownItemType_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/backlog/suggest",
            new SuggestContentRequest(
                Field: "description",
                ItemType: "initiative",
                Title: "Platform",
                CurrentContent: null,
                RepositoryId: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Suggest_NoLlmSetting_Returns400()
    {
        // No repositoryId AND no default LlmSetting for dev-user →
        // the service returns NoLlmSetting, controller maps to 400.
        var response = await _client.PostAsJsonAsync(
            "/api/backlog/suggest",
            new SuggestContentRequest(
                Field: "description",
                ItemType: "story",
                Title: "Login",
                CurrentContent: null,
                RepositoryId: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Suggest_RepoDoesNotExist_Returns404()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/backlog/suggest",
            new SuggestContentRequest(
                Field: "description",
                ItemType: "story",
                Title: "Login",
                CurrentContent: null,
                RepositoryId: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Suggest_RequiresTitle()
    {
        // `Title` is [Required] — empty/missing should fail validation
        // before ever reaching the service.
        var response = await _client.PostAsJsonAsync(
            "/api/backlog/suggest",
            new SuggestContentRequest(
                Field: "description",
                ItemType: "story",
                Title: "",
                CurrentContent: null,
                RepositoryId: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
