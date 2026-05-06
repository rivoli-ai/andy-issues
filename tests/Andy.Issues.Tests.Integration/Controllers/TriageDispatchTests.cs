// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

// Z2 (#111) — end-to-end smoke for the dispatch path. Verifies the
// controller, IAgentsClient, IContainersClient, IssueService, and
// EF wiring all line up so a configured agent triggers a real run
// dispatch and `Issue.TriageRunId` lands on the row.
public class TriageDispatchTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TriageDispatchTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<Guid> CreateIssueAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/triage",
            new CreateIssueRequest("intake", "body", null), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        return dto!.Id;
    }

    [Fact]
    public async Task Start_AgentConfigured_PersistsRunIdOnIssue()
    {
        var issueId = await CreateIssueAsync();
        var runId = Guid.NewGuid();

        _factory.FakeAgentsClient.Reset();
        _factory.FakeAgentsClient.TriageAgent = new AgentDescriptor("triage-agent-v1", "1.0");
        _factory.FakeContainersClient.Reset();
        _factory.FakeContainersClient.HeadlessRunResult = new HeadlessRunResponse(runId);

        var resp = await _client.PostAsync($"/api/triage/{issueId}/start", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var call = Assert.Single(_factory.FakeContainersClient.HeadlessRunCalls);
        Assert.Equal("triage-agent-v1", call.AgentId);
        Assert.Equal(issueId, call.IssueId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var triageRunId = await db.Issues
            .AsNoTracking()
            .Where(i => i.Id == issueId)
            .Select(i => i.TriageRunId)
            .FirstAsync();
        Assert.Equal(runId, triageRunId);
    }

    [Fact]
    public async Task Start_NoAgentConfigured_StillTransitions_NoDispatch()
    {
        var issueId = await CreateIssueAsync();

        _factory.FakeAgentsClient.Reset();   // no TriageAgent
        _factory.FakeContainersClient.Reset();

        var resp = await _client.PostAsync($"/api/triage/{issueId}/start", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.Equal("Triaging", dto!.TriageState);
        Assert.Empty(_factory.FakeContainersClient.HeadlessRunCalls);
    }
}
