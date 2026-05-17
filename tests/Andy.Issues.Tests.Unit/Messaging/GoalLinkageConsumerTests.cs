// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text;
using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Messaging.Consumers;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Messaging;

// AH6 (rivoli-ai/conductor#713) — GoalLinkageConsumer drains
// andy.tasks.events.goal.{id}.created events and writes the
// reverse pin onto the originating Issue.
public sealed class GoalLinkageConsumerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly ServiceProvider _sp;

    public GoalLinkageConsumerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        using (var ctx = new AppDbContext(_options))
            ctx.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddSingleton(_options);
        services.AddScoped<AppDbContext>();
        services.AddLogging();
        _sp = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task PinsGoalDisplayId_OnMatchingIssue()
    {
        var issue = await CreateIssueAsync();

        var consumer = NewConsumer();
        var msg = BuildMessage(
            subject: $"andy.tasks.events.goal.{Guid.NewGuid()}.created",
            new GoalCreatedEventPayload(
                GoalId: Guid.NewGuid(),
                GoalDisplayId: "GOAL-42",
                SourceIssueDisplayId: issue.DisplayId));

        await consumer.HandleAsync(msg, default);

        Assert.True(msg.Acked);
        using var verify = new AppDbContext(_options);
        var refreshed = await verify.Issues.FirstAsync(i => i.Id == issue.Id);
        Assert.Equal("GOAL-42", refreshed.GoalDisplayId);
    }

    [Fact]
    public async Task IgnoresPayload_WithNoSourceIssueDisplayId()
    {
        var issue = await CreateIssueAsync();

        var consumer = NewConsumer();
        var msg = BuildMessage(
            subject: $"andy.tasks.events.goal.{Guid.NewGuid()}.created",
            new GoalCreatedEventPayload(
                GoalId: Guid.NewGuid(),
                GoalDisplayId: "GOAL-9",
                SourceIssueDisplayId: null));

        await consumer.HandleAsync(msg, default);

        Assert.True(msg.Acked);
        using var verify = new AppDbContext(_options);
        var refreshed = await verify.Issues.FirstAsync(i => i.Id == issue.Id);
        Assert.Null(refreshed.GoalDisplayId);
    }

    [Fact]
    public async Task IgnoresNonIssueSource_LikeSTORYorEPIC()
    {
        // The consumer only owns the ISSUE-N prefix. STORY/EPIC/FEAT
        // sources are out of scope (backlog-entity linkage, separate
        // surface). They must not throw and must not write to Issues.
        var issue = await CreateIssueAsync();

        var consumer = NewConsumer();
        var msg = BuildMessage(
            subject: $"andy.tasks.events.goal.{Guid.NewGuid()}.created",
            new GoalCreatedEventPayload(
                GoalId: Guid.NewGuid(),
                GoalDisplayId: "GOAL-77",
                SourceIssueDisplayId: "STORY-12"));

        await consumer.HandleAsync(msg, default);

        Assert.True(msg.Acked);
        using var verify = new AppDbContext(_options);
        var refreshed = await verify.Issues.FirstAsync(i => i.Id == issue.Id);
        Assert.Null(refreshed.GoalDisplayId);
    }

    [Fact]
    public async Task IsIdempotent_OnRedelivery()
    {
        var issue = await CreateIssueAsync();

        var consumer = NewConsumer();

        // First delivery — writes the link.
        await consumer.HandleAsync(BuildMessage(
            subject: $"andy.tasks.events.goal.{Guid.NewGuid()}.created",
            new GoalCreatedEventPayload(
                GoalId: Guid.NewGuid(),
                GoalDisplayId: "GOAL-100",
                SourceIssueDisplayId: issue.DisplayId)), default);

        // Second delivery (same source + goal, different msg-id) —
        // should be a no-op write-wise.
        var msg2 = BuildMessage(
            subject: $"andy.tasks.events.goal.{Guid.NewGuid()}.created",
            new GoalCreatedEventPayload(
                GoalId: Guid.NewGuid(),
                GoalDisplayId: "GOAL-100",
                SourceIssueDisplayId: issue.DisplayId));
        await consumer.HandleAsync(msg2, default);

        Assert.True(msg2.Acked);
        using var verify = new AppDbContext(_options);
        var refreshed = await verify.Issues.FirstAsync(i => i.Id == issue.Id);
        Assert.Equal("GOAL-100", refreshed.GoalDisplayId);
    }

    [Fact]
    public async Task RefusesToOverwrite_AnExistingDifferentLink()
    {
        // Already-linked issues are a data-integrity signal — log +
        // ack rather than silently overwrite. The conductor's "find
        // issues that became goals" query path would otherwise show
        // a different goal after the second delivery.
        var issue = await CreateIssueAsync();
        using (var ctx = new AppDbContext(_options))
        {
            var i = await ctx.Issues.FirstAsync(x => x.Id == issue.Id);
            i.GoalDisplayId = "GOAL-1";
            await ctx.SaveChangesAsync();
        }

        var consumer = NewConsumer();
        var msg = BuildMessage(
            subject: $"andy.tasks.events.goal.{Guid.NewGuid()}.created",
            new GoalCreatedEventPayload(
                GoalId: Guid.NewGuid(),
                GoalDisplayId: "GOAL-2",
                SourceIssueDisplayId: issue.DisplayId));

        await consumer.HandleAsync(msg, default);

        Assert.True(msg.Acked);
        using var verify = new AppDbContext(_options);
        var refreshed = await verify.Issues.FirstAsync(i => i.Id == issue.Id);
        Assert.Equal("GOAL-1", refreshed.GoalDisplayId);  // unchanged
    }

    [Fact]
    public async Task SkipsNonCreatedSubjects_LikeGoalCompleted()
    {
        // The consumer is registered on the broader `goal.>` prefix
        // so it sees goal.completed / goal.planned events too. Those
        // don't carry a SourceIssueDisplayId — even if they did, we
        // only want to write the link on the .created event.
        var issue = await CreateIssueAsync();

        var consumer = NewConsumer();
        var msg = BuildMessage(
            subject: $"andy.tasks.events.goal.{Guid.NewGuid()}.completed",
            new GoalCreatedEventPayload(
                GoalId: Guid.NewGuid(),
                GoalDisplayId: "GOAL-50",
                SourceIssueDisplayId: issue.DisplayId));

        await consumer.HandleAsync(msg, default);

        Assert.True(msg.Acked);
        using var verify = new AppDbContext(_options);
        var refreshed = await verify.Issues.FirstAsync(i => i.Id == issue.Id);
        Assert.Null(refreshed.GoalDisplayId);  // .completed didn't write
    }

    [Fact]
    public async Task NoMatchingIssue_AcksWithoutThrowing()
    {
        var consumer = NewConsumer();
        var msg = BuildMessage(
            subject: $"andy.tasks.events.goal.{Guid.NewGuid()}.created",
            new GoalCreatedEventPayload(
                GoalId: Guid.NewGuid(),
                GoalDisplayId: "GOAL-31",
                SourceIssueDisplayId: "ISSUE-9999"));

        await consumer.HandleAsync(msg, default);

        Assert.True(msg.Acked);
        // No issue to refresh — just confirm we didn't blow up.
    }

    // ----- helpers ------------------------------------------------------

    private GoalLinkageConsumer NewConsumer() => new(
        _sp.GetRequiredService<IServiceScopeFactory>(),
        new DummyBus(),
        NullLogger<GoalLinkageConsumer>.Instance);

    private async Task<Issue> CreateIssueAsync()
    {
        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = new IssueService(
            ctx,
            new StubDocsClient(),
            new BacklogSequenceAllocator(ctx));
        var dto = await svc.CreateAsync(
            new CreateIssueRequest(Title: "linkage", Body: null, RepositoryId: null),
            "alice");
        return await ctx.Issues.AsNoTracking().FirstAsync(i => i.Id == dto.Id);
    }

    private static FakeIncomingMessage BuildMessage(string subject, GoalCreatedEventPayload payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, EventJson.Options));
        return new FakeIncomingMessage
        {
            Subject = subject,
            Headers = MessageHeaders.NewRoot(),
            Payload = bytes,
            ReceivedAt = DateTimeOffset.UtcNow,
        };
    }

    private sealed class FakeIncomingMessage : IncomingMessage
    {
        public bool Acked { get; private set; }

        public override Task AckAsync(CancellationToken ct = default)
        {
            Acked = true;
            return Task.CompletedTask;
        }

        public override Task NackAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class DummyBus : IMessageBus
    {
        public Task PublishAsync(string subject, object payload, MessageHeaders headers, CancellationToken ct = default)
            => Task.CompletedTask;
        public IAsyncEnumerable<IncomingMessage> SubscribeAsync(
            string subjectFilter, SubscriptionOptions options, CancellationToken ct = default)
            => throw new NotSupportedException("Not exercised in unit tests.");
    }

    private sealed class StubDocsClient : IDocsClient
    {
        public Task<bool> VerifyLinkAsync(
            Guid linkId, string expectedTargetType, Guid expectedTargetId,
            CancellationToken ct = default) => Task.FromResult(true);

        public Task<DocsMetadata?> GetMetadataAsync(
            Guid documentId, CancellationToken ct = default)
            => Task.FromResult<DocsMetadata?>(null);
    }
}
