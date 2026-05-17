// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

// Z2 (#111) — agent dispatch behavior in StartTriageAsync.
// Sibling to IssueServiceTests.cs; that file covers the state-machine
// surface, this one covers the dispatch surface added on top.
public class IssueServiceTriageDispatchTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public IssueServiceTriageDispatchTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new AppDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext NewContext() => new(_options);

    private (IssueService Service, FakeAgentsClient Agents, FakeContainersClient Containers) NewService(
        AppDbContext ctx,
        AgentDescriptor? agent = null)
    {
        var agents = new FakeAgentsClient { TriageAgent = agent };
        var containers = new FakeContainersClient();
        var service = new IssueService(
            ctx,
            new TriageDispatchTestDocsClient(),
            new BacklogSequenceAllocator(ctx),
            estimator: null,
            agents: agents,
            containers: containers,
            logger: null);
        return (service, agents, containers);
    }

    private async Task<Guid> CreateIssueAsync(string owner = "alice")
    {
        await using var ctx = NewContext();
        var (svc, _, _) = NewService(ctx);
        var dto = await svc.CreateAsync(new CreateIssueRequest("intake", "body", null), owner);
        return dto.Id;
    }

    [Fact]
    public async Task StartTriage_AgentConfigured_DispatchesAndPersistsRunId()
    {
        var id = await CreateIssueAsync();
        var runId = Guid.NewGuid();

        await using var ctx = NewContext();
        var (svc, _, containers) = NewService(
            ctx, agent: new AgentDescriptor("triage-agent-v1", "1.0"));
        containers.HeadlessRunResult = new HeadlessRunResponse(runId);

        var result = await svc.StartTriageAsync(id, "alice");

        Assert.Equal(IssueTriageOutcome.Updated, result.Outcome);
        await using var verify = NewContext();
        var issue = await verify.Issues.SingleAsync(i => i.Id == id);
        Assert.Equal(TriageState.Triaging, issue.TriageState);
        Assert.Equal(runId, issue.TriageRunId);

        var call = Assert.Single(containers.HeadlessRunCalls);
        Assert.Equal("triage-agent-v1", call.AgentId);
        Assert.Equal("1.0", call.AgentVersion);
        Assert.Equal(id, call.IssueId);
        Assert.Null(call.StoryId);
        // Owner becomes the tenant axis until a real tenant model lands.
        Assert.Equal("alice", call.TenantId);
    }

    [Fact]
    public async Task StartTriage_NoAgentConfigured_TransitionsButLeavesRunIdNull()
    {
        var id = await CreateIssueAsync();

        await using var ctx = NewContext();
        var (svc, _, containers) = NewService(ctx, agent: null);

        var result = await svc.StartTriageAsync(id, "alice");

        Assert.Equal(IssueTriageOutcome.Updated, result.Outcome);
        await using var verify = NewContext();
        var issue = await verify.Issues.SingleAsync(i => i.Id == id);
        Assert.Equal(TriageState.Triaging, issue.TriageState);
        Assert.Null(issue.TriageRunId);
        Assert.Empty(containers.HeadlessRunCalls);
    }

    [Fact]
    public async Task StartTriage_ContainerReturnsNull_LeavesRunIdNullButTransitions()
    {
        var id = await CreateIssueAsync();

        await using var ctx = NewContext();
        var (svc, _, containers) = NewService(
            ctx, agent: new AgentDescriptor("triage-agent-v1"));
        containers.HeadlessRunResult = null;

        var result = await svc.StartTriageAsync(id, "alice");

        Assert.Equal(IssueTriageOutcome.Updated, result.Outcome);
        await using var verify = NewContext();
        var issue = await verify.Issues.SingleAsync(i => i.Id == id);
        Assert.Equal(TriageState.Triaging, issue.TriageState);
        Assert.Null(issue.TriageRunId);
        Assert.Single(containers.HeadlessRunCalls);
    }

    [Fact]
    public async Task StartTriage_ContainerThrows_DoesNotBubble_StateTransitions()
    {
        var id = await CreateIssueAsync();

        await using var ctx = NewContext();
        var (svc, _, containers) = NewService(
            ctx, agent: new AgentDescriptor("triage-agent-v1"));
        containers.HeadlessRunException = new HttpRequestException("boom");

        var result = await svc.StartTriageAsync(id, "alice");

        Assert.Equal(IssueTriageOutcome.Updated, result.Outcome);
        await using var verify = NewContext();
        var issue = await verify.Issues.SingleAsync(i => i.Id == id);
        Assert.Equal(TriageState.Triaging, issue.TriageState);
        Assert.Null(issue.TriageRunId);
    }

    [Fact]
    public async Task StartTriage_AlreadyTriaging_IsIdempotent_NoDuplicateDispatch()
    {
        var id = await CreateIssueAsync();
        var firstRunId = Guid.NewGuid();

        // First call: dispatches and persists run id.
        await using (var ctx = NewContext())
        {
            var (svc, _, containers) = NewService(
                ctx, agent: new AgentDescriptor("triage-agent-v1"));
            containers.HeadlessRunResult = new HeadlessRunResponse(firstRunId);
            await svc.StartTriageAsync(id, "alice");
        }

        // Second call on already-Triaging issue: short-circuits before
        // dispatch. RunId stays at the first value; no second container
        // call is made.
        await using (var ctx = NewContext())
        {
            var (svc, _, containers) = NewService(
                ctx, agent: new AgentDescriptor("triage-agent-v1"));
            containers.HeadlessRunResult = new HeadlessRunResponse(Guid.NewGuid());

            var result = await svc.StartTriageAsync(id, "alice");

            Assert.Equal(IssueTriageOutcome.Updated, result.Outcome);
            Assert.Empty(containers.HeadlessRunCalls);
        }

        await using var verify = NewContext();
        var issue = await verify.Issues.SingleAsync(i => i.Id == id);
        Assert.Equal(firstRunId, issue.TriageRunId);
    }

    [Fact]
    public async Task StartTriage_WrongOwner_Returns404_NoDispatch()
    {
        var id = await CreateIssueAsync(owner: "alice");

        await using var ctx = NewContext();
        var (svc, _, containers) = NewService(
            ctx, agent: new AgentDescriptor("triage-agent-v1"));

        var result = await svc.StartTriageAsync(id, "bob");

        Assert.Equal(IssueTriageOutcome.NotFound, result.Outcome);
        Assert.Empty(containers.HeadlessRunCalls);
    }

    [Fact]
    public async Task StartTriage_UnknownIssue_Returns404_NoDispatch()
    {
        await using var ctx = NewContext();
        var (svc, _, containers) = NewService(
            ctx, agent: new AgentDescriptor("triage-agent-v1"));

        var result = await svc.StartTriageAsync(Guid.NewGuid(), "alice");

        Assert.Equal(IssueTriageOutcome.NotFound, result.Outcome);
        Assert.Empty(containers.HeadlessRunCalls);
    }

    [Fact]
    public async Task StartTriage_PreAttachedDocs_AreIncludedInRunRequest()
    {
        var id = await CreateIssueAsync();
        var linkId1 = Guid.NewGuid();
        var linkId2 = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.IssueAttachments.AddRange(
                new IssueAttachment
                {
                    IssueId = id,
                    LinkId = linkId1,
                    DocumentId = Guid.NewGuid(),
                    CreatedBy = "alice",
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new IssueAttachment
                {
                    IssueId = id,
                    LinkId = linkId2,
                    DocumentId = Guid.NewGuid(),
                    CreatedBy = "alice",
                    CreatedAt = DateTimeOffset.UtcNow
                });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        var (svc, _, containers) = NewService(
            ctx2, agent: new AgentDescriptor("triage-agent-v1"));

        await svc.StartTriageAsync(id, "alice");

        var call = Assert.Single(containers.HeadlessRunCalls);
        Assert.NotNull(call.InputDocRefs);
        Assert.Equal(2, call.InputDocRefs!.Count);
        Assert.Contains(linkId1.ToString(), call.InputDocRefs);
        Assert.Contains(linkId2.ToString(), call.InputDocRefs);
    }
}

internal sealed class FakeAgentsClient : IAgentsClient
{
    public AgentDescriptor? TriageAgent { get; set; }
    public Exception? Throw { get; set; }
    public int CallCount { get; private set; }

    public Task<AgentDescriptor?> GetTriageAgentAsync(CancellationToken ct = default)
    {
        CallCount++;
        if (Throw is not null) throw Throw;
        return Task.FromResult(TriageAgent);
    }
}

internal sealed class TriageDispatchTestDocsClient : IDocsClient
{
    public Task<bool> VerifyLinkAsync(Guid linkId, string expectedTargetType, Guid expectedTargetId, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<DocsMetadata?> GetMetadataAsync(Guid documentId, CancellationToken ct = default) =>
        Task.FromResult<DocsMetadata?>(null);
}
