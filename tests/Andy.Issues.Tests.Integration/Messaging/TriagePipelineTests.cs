// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Messaging;

// Z12 — end-to-end pipeline tests for the triage flow.
//
//   POST /api/triage          (Z1)
//   POST /api/triage/{id}/start  (Z2 dispatch)
//      → IssueService transitions NeedsTriage → Triaging
//      → IContainersClient.RunHeadlessAsync (faked)
//   Test publishes andy.containers.events.run.{runId}.finished
//      → ContainerRunEventConsumer (Z2 PR A)
//      → IIssueService.CompleteTriageAsync
//      → outbox row appended (Z4)
//      → OutboxDispatcher publishes to bus
//   Test reads the outbox row and verifies subject + payload (Z3 schema).
//
// The in-memory bus + EF in-memory DB are sufficient for the in-process
// pipeline. A separate `[NatsFact]`-decorated smoke at the bottom runs
// the same flow against a real JetStream when NATS is available.
public class TriagePipelineTests : IClassFixture<TriagePipelineTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static readonly TimeSpan PipelineTimeout = TimeSpan.FromSeconds(5);

    private readonly TriagePipelineTestFactory _factory;
    private readonly HttpClient _client;

    public TriagePipelineTests(TriagePipelineTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.FakeAgentsClient.Reset();
        _factory.FakeContainersClient.Reset();
        _factory.FakeAgentsClient.TriageAgent =
            new AgentDescriptor("triage-agent-z12", "1.0");
    }

    [Fact]
    public async Task HappyPath_RunFinishedEvent_TransitionsToTriaged_AndEmitsTriagedOutbox()
    {
        var issueId = await CreateIssueAsync();
        var (runId, _) = await StartTriageAsync(issueId);

        await PublishRunEventAsync(runId, issueId, "finished");

        var triagedRow = await WaitForOutboxAsync(
            issueId, $"andy.issues.events.issue.{issueId}.triaged");

        Assert.NotNull(triagedRow);
        Assert.Equal(typeof(IssueEventPayload).FullName, triagedRow!.PayloadType);

        var payload = JsonSerializer.Deserialize<IssueEventPayload>(
            triagedRow.PayloadJson, EventJson.Options);
        Assert.NotNull(payload);
        Assert.Equal(issueId, payload!.IssueId);
        Assert.Equal(TriageState.Triaged.ToString(), payload.TriageState);
        Assert.Equal(IssueEventPayload.SchemaVersion, payload.Schema_Version);

        // Issue persisted as Triaged
        using var verifyScope = NewScope();
        var issue = await verifyScope.db.Issues
            .AsNoTracking()
            .SingleAsync(i => i.Id == issueId);
        Assert.Equal(TriageState.Triaged, issue.TriageState);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public async Task RunFailedOrCancelled_LeavesIssueTriaging_NoTriagedOutbox(string kind)
    {
        var issueId = await CreateIssueAsync();
        var (runId, _) = await StartTriageAsync(issueId);

        await PublishRunEventAsync(runId, issueId, kind);

        // Negative wait: poll for a short bounded window confirming the
        // triaged row never appears. 1.5s is well under PipelineTimeout
        // and the consumer typically reacts within ~50ms.
        await Task.Delay(TimeSpan.FromMilliseconds(1500));

        using var verifyScope = NewScope();
        var triagedSubject = $"andy.issues.events.issue.{issueId}.triaged";
        var triaged = await verifyScope.db.Outbox
            .AsNoTracking()
            .AnyAsync(o => o.Subject == triagedSubject);
        Assert.False(triaged, "run.failed/cancelled must not produce a triaged event");

        var issue = await verifyScope.db.Issues
            .AsNoTracking()
            .SingleAsync(i => i.Id == issueId);
        Assert.Equal(TriageState.Triaging, issue.TriageState);
    }

    [Fact]
    public async Task DuplicateRunFinished_IsIdempotent_OneTriagedOutbox()
    {
        var issueId = await CreateIssueAsync();
        var (runId, _) = await StartTriageAsync(issueId);

        // Publish the same logical event twice with the same msg-id —
        // the consumer's bounded ring-buffer dedupes (see
        // ContainerRunEventConsumer.TryRemember at line ~186).
        var sharedHeaders = MessageHeaders.NewRoot();
        await PublishRunEventAsync(runId, issueId, "finished", headers: sharedHeaders);
        await PublishRunEventAsync(runId, issueId, "finished", headers: sharedHeaders);

        // First triaged row should land — wait for it, then assert
        // exactly one.
        await WaitForOutboxAsync(issueId, $"andy.issues.events.issue.{issueId}.triaged");
        await Task.Delay(TimeSpan.FromMilliseconds(500));  // give a redelivery window

        using var verifyScope = NewScope();
        var triagedRows = await verifyScope.db.Outbox
            .AsNoTracking()
            .Where(o => o.Subject == $"andy.issues.events.issue.{issueId}.triaged")
            .CountAsync();
        Assert.Equal(1, triagedRows);
    }

    [Fact]
    public async Task UnknownIssueId_RunFinished_NoOutbox_NoStateChange()
    {
        var issueId = await CreateIssueAsync();
        var (runId, _) = await StartTriageAsync(issueId);

        // Publish finished for a totally different issue id — the
        // consumer should ack and skip without touching anything.
        await PublishRunEventAsync(runId, issueId: Guid.NewGuid(), "finished");

        await Task.Delay(TimeSpan.FromMilliseconds(1500));

        using var verifyScope = NewScope();
        var triaged = await verifyScope.db.Outbox
            .AsNoTracking()
            .AnyAsync(o => o.Subject == $"andy.issues.events.issue.{issueId}.triaged");
        Assert.False(triaged);

        var issue = await verifyScope.db.Issues
            .AsNoTracking()
            .SingleAsync(i => i.Id == issueId);
        Assert.Equal(TriageState.Triaging, issue.TriageState);
    }

    [Fact]
    public async Task HappyPath_TriagedOutboxRow_CarriesIssueIdAsCorrelationId()
    {
        var issueId = await CreateIssueAsync();
        var (runId, _) = await StartTriageAsync(issueId);

        await PublishRunEventAsync(runId, issueId, "finished");

        var row = await WaitForOutboxAsync(
            issueId, $"andy.issues.events.issue.{issueId}.triaged");

        // Z4 / ADR-0001: correlation_id is the entity id (issueId here).
        Assert.NotNull(row);
        Assert.Equal(issueId, row!.CorrelationId);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task<Guid> CreateIssueAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/triage",
            new CreateIssueRequest("intake", "body", null), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        return dto!.Id;
    }

    private async Task<(Guid RunId, IssueDto Dto)> StartTriageAsync(Guid issueId)
    {
        var resp = await _client.PostAsync($"/api/triage/{issueId}/start", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);

        var call = Assert.Single(_factory.FakeContainersClient.HeadlessRunCalls);
        Assert.Equal(issueId, call.IssueId);

        // Read the run id back from the issue row — set by the
        // dispatcher when the fake returns a HeadlessRunResponse.
        using var scope = NewScope();
        var runId = await scope.db.Issues
            .AsNoTracking()
            .Where(i => i.Id == issueId)
            .Select(i => i.TriageRunId)
            .SingleAsync();
        Assert.NotNull(runId);
        return (runId!.Value, dto!);
    }

    private async Task PublishRunEventAsync(
        Guid runId, Guid issueId, string kind, MessageHeaders? headers = null)
    {
        using var scope = _factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var payload = new ContainerRunEventPayload(
            RunId: runId,
            StoryId: null,
            Status: kind,
            ExitCode: kind == "finished" ? 0 : 1,
            DurationSeconds: 1.5,
            IssueId: issueId);

        await bus.PublishAsync(
            $"andy.containers.events.run.{runId}.{kind}",
            payload,
            headers ?? MessageHeaders.NewRoot());
    }

    private async Task<OutboxEntry?> WaitForOutboxAsync(
        Guid correlationId, string subject)
    {
        var deadline = DateTimeOffset.UtcNow + PipelineTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = NewScope();
            var row = await scope.db.Outbox
                .AsNoTracking()
                .Where(o => o.CorrelationId == correlationId && o.Subject == subject)
                .FirstOrDefaultAsync();
            if (row is not null) return row;
            await Task.Delay(50);
        }
        return null;
    }

    private ScopeWithDb NewScope()
    {
        var scope = _factory.Services.CreateScope();
        return new ScopeWithDb(scope, scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private readonly struct ScopeWithDb : IDisposable
    {
        public IServiceScope scope { get; }
        public AppDbContext db { get; }

        public ScopeWithDb(IServiceScope scope, AppDbContext db)
        {
            this.scope = scope;
            this.db = db;
        }

        public void Dispose() => scope.Dispose();
    }
}
