// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

// Z1 — REST round-trip per state-machine transition. The TestAuthHandler
// authenticates every request as `dev-user`, so issue ownership is
// satisfied by default. Each terminal transition asserts that an outbox
// row was appended in the same unit of work.
public class TriageControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TriageControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<List<string>> GetOutboxSubjectsAsync(Guid issueId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Outbox
            .Where(o => o.CorrelationId == issueId)
            .OrderBy(o => o.CreatedAt)
            .Select(o => o.Subject)
            .ToListAsync();
    }

    private async Task<IssueDto> CreateIssueAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/triage",
            new CreateIssueRequest("intake", "body", null));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        return dto!;
    }

    [Fact]
    public async Task FullLifecycle_AcceptPath_EmitsTriagedAndAccepted()
    {
        var issue = await CreateIssueAsync();
        Assert.Equal("NeedsTriage", issue.TriageState);

        var startResp = await _client.PostAsync($"/api/triage/{issue.Id}/start", null);
        Assert.Equal(HttpStatusCode.OK, startResp.StatusCode);
        var afterStart = await startResp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.Equal("Triaging", afterStart!.TriageState);

        var completeResp = await _client.PostAsync($"/api/triage/{issue.Id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);
        var afterComplete = await completeResp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.Equal("Triaged", afterComplete!.TriageState);
        Assert.NotNull(afterComplete.TriagedAt);

        var acceptResp = await _client.PostAsync($"/api/triage/{issue.Id}/accept", null);
        Assert.Equal(HttpStatusCode.OK, acceptResp.StatusCode);
        var afterAccept = await acceptResp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.Equal("Accepted", afterAccept!.TriageState);

        var subjects = await GetOutboxSubjectsAsync(issue.Id);
        Assert.Equal(2, subjects.Count);
        Assert.EndsWith(".triaged", subjects[0]);
        Assert.EndsWith(".accepted", subjects[1]);
    }

    [Fact]
    public async Task FullLifecycle_RejectPath_EmitsTriagedAndRejected()
    {
        var issue = await CreateIssueAsync();
        await _client.PostAsync($"/api/triage/{issue.Id}/start", null);
        await _client.PostAsync($"/api/triage/{issue.Id}/complete", null);
        var rejectResp = await _client.PostAsync($"/api/triage/{issue.Id}/reject", null);
        Assert.Equal(HttpStatusCode.OK, rejectResp.StatusCode);
        var afterReject = await rejectResp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.Equal("Rejected", afterReject!.TriageState);

        var subjects = await GetOutboxSubjectsAsync(issue.Id);
        Assert.EndsWith(".triaged", subjects[0]);
        Assert.EndsWith(".rejected", subjects[1]);
    }

    [Fact]
    public async Task InvalidTransition_Returns409()
    {
        var issue = await CreateIssueAsync();
        // Cannot accept directly from NeedsTriage.
        var resp = await _client.PostAsync($"/api/triage/{issue.Id}/accept", null);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task UnknownIssue_Returns404()
    {
        var resp = await _client.PostAsync($"/api/triage/{Guid.NewGuid()}/start", null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsIssue()
    {
        var issue = await CreateIssueAsync();
        var resp = await _client.GetAsync($"/api/triage/{issue.Id}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.Equal(issue.Id, dto!.Id);
    }

    [Fact]
    public async Task ReInvokeTriage_FromTriaged_TransitionsBackToTriaging()
    {
        var issue = await CreateIssueAsync();
        await _client.PostAsync($"/api/triage/{issue.Id}/start", null);
        await _client.PostAsync($"/api/triage/{issue.Id}/complete", null);

        // Re-invoke via Z9/Z10 path goes through the same `start` endpoint.
        var reStart = await _client.PostAsync($"/api/triage/{issue.Id}/start", null);
        Assert.Equal(HttpStatusCode.OK, reStart.StatusCode);
        var dto = await reStart.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.Equal("Triaging", dto!.TriageState);
    }
}
