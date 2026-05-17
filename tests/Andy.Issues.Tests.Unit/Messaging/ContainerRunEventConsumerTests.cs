// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.External;
using Andy.Issues.Infrastructure.Messaging.Consumers;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Messaging;

public class ContainerRunEventConsumerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly ServiceProvider _sp;

    public ContainerRunEventConsumerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
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
        // Z2 — IssueId-correlated runs call IIssueService.CompleteTriageAsync
        // to drive the state machine + outbox + Z7 estimator backfill.
        services.AddScoped<ITriageEstimator, NoopTriageEstimator>();
        services.AddSingleton<IDocsClient, StubDocsClient>();
        // AH6: IssueService now depends on IBacklogSequenceAllocator
        // to stamp ISSUE-N on the new issue. Wire the real allocator
        // — it works against the same in-memory SQLite.
        services.AddScoped<IBacklogSequenceAllocator,
            Andy.Issues.Infrastructure.Services.BacklogSequenceAllocator>();
        services.AddScoped<IIssueService, IssueService>();
        _sp = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<(Guid repoId, Guid epicId, Guid featureId, Guid storyId)> SeedStoryAsync(
        UserStoryStatus initial = UserStoryStatus.InProgress)
    {
        await using var ctx = new AppDbContext(_options);
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "repo",
            CloneUrl = "https://example.com/r.git"
        };
        var epic = new Epic { Id = Guid.NewGuid(), RepositoryId = repo.Id, Title = "E", Order = 1 };
        var feature = new Feature { Id = Guid.NewGuid(), EpicId = epic.Id, Title = "F", Order = 1 };
        var story = new UserStory
        {
            Id = Guid.NewGuid(),
            FeatureId = feature.Id,
            Title = "s",
            Order = 1
        };
        if (initial != UserStoryStatus.Draft)
        {
            // Draft → Ready → InProgress is the canonical path.
            if (initial >= UserStoryStatus.Ready) story.SetStatus(UserStoryStatus.Ready);
            if (initial >= UserStoryStatus.InProgress) story.SetStatus(UserStoryStatus.InProgress);
            if (initial >= UserStoryStatus.InReview) story.SetStatus(UserStoryStatus.InReview);
            if (initial == UserStoryStatus.Done) story.SetStatus(UserStoryStatus.Done);
        }
        ctx.Repositories.Add(repo);
        ctx.Epics.Add(epic);
        ctx.Features.Add(feature);
        ctx.UserStories.Add(story);
        await ctx.SaveChangesAsync();
        return (repo.Id, epic.Id, feature.Id, story.Id);
    }

    private ContainerRunEventConsumer NewConsumer()
    {
        return new ContainerRunEventConsumer(
            _sp.GetRequiredService<IServiceScopeFactory>(),
            new DummyBus(),
            NullLogger<ContainerRunEventConsumer>.Instance);
    }

    private static FakeIncomingMessage BuildMessage(Guid? storyId, Guid runId, string kind)
    {
        var payload = new ContainerRunEventPayload(runId, storyId, kind, null, null);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, EventJson.Options);
        return new FakeIncomingMessage
        {
            Headers = MessageHeaders.NewRoot(),
            Subject = $"andy.containers.events.run.{runId}.{kind}",
            Payload = bytes,
            ReceivedAt = DateTimeOffset.UtcNow
        };
    }

    private static FakeIncomingMessage BuildIssueMessage(Guid issueId, Guid runId, string kind)
    {
        var payload = new ContainerRunEventPayload(
            runId, StoryId: null, Status: kind, ExitCode: null, DurationSeconds: null,
            IssueId: issueId);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, EventJson.Options);
        return new FakeIncomingMessage
        {
            Headers = MessageHeaders.NewRoot(),
            Subject = $"andy.containers.events.run.{runId}.{kind}",
            Payload = bytes,
            ReceivedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<Guid> SeedIssueAsync(TriageState state)
    {
        await using var ctx = new AppDbContext(_options);
        var issue = new Issue
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Title = "needs-triage"
        };
        // The state machine validates each transition, so step through
        // the legal path to reach the desired starting state.
        if (state == TriageState.Triaging)
        {
            issue.StartTriage();
        }
        else if (state == TriageState.Triaged)
        {
            issue.StartTriage();
            issue.CompleteTriage("alice");
        }
        ctx.Issues.Add(issue);
        await ctx.SaveChangesAsync();
        return issue.Id;
    }

    [Fact]
    public async Task Finished_TransitionsStoryToInReview()
    {
        var seed = await SeedStoryAsync(UserStoryStatus.InProgress);
        var msg = BuildMessage(seed.storyId, Guid.NewGuid(), "finished");

        await NewConsumer().HandleAsync(msg, CancellationToken.None);

        await using var verify = new AppDbContext(_options);
        var story = await verify.UserStories.FirstAsync();
        Assert.Equal(UserStoryStatus.InReview, story.Status);
        Assert.True(msg.Acked);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public async Task FailedOrCancelled_LeavesStatusUnchanged(string kind)
    {
        var seed = await SeedStoryAsync(UserStoryStatus.InProgress);
        var msg = BuildMessage(seed.storyId, Guid.NewGuid(), kind);

        await NewConsumer().HandleAsync(msg, CancellationToken.None);

        await using var verify = new AppDbContext(_options);
        var story = await verify.UserStories.FirstAsync();
        Assert.Equal(UserStoryStatus.InProgress, story.Status);
        Assert.True(msg.Acked);
    }

    [Fact]
    public async Task StoryAlreadyDone_DoesNotRegress()
    {
        var seed = await SeedStoryAsync(UserStoryStatus.Done);
        var msg = BuildMessage(seed.storyId, Guid.NewGuid(), "finished");

        await NewConsumer().HandleAsync(msg, CancellationToken.None);

        await using var verify = new AppDbContext(_options);
        var story = await verify.UserStories.FirstAsync();
        Assert.Equal(UserStoryStatus.Done, story.Status);
    }

    [Fact]
    public async Task UnknownStoryId_AcksAndSkips()
    {
        await SeedStoryAsync();
        var msg = BuildMessage(Guid.NewGuid(), Guid.NewGuid(), "finished");  // unknown story

        await NewConsumer().HandleAsync(msg, CancellationToken.None);

        Assert.True(msg.Acked);
    }

    [Fact]
    public async Task NullStoryId_AcksAndSkips()
    {
        var seed = await SeedStoryAsync(UserStoryStatus.InProgress);
        var msg = BuildMessage(storyId: null, Guid.NewGuid(), "finished");

        await NewConsumer().HandleAsync(msg, CancellationToken.None);

        await using var verify = new AppDbContext(_options);
        var story = await verify.UserStories.FirstAsync();
        Assert.Equal(UserStoryStatus.InProgress, story.Status);  // unchanged
        Assert.True(msg.Acked);
    }

    [Fact]
    public async Task DuplicateMsgId_ProcessedOnlyOnce()
    {
        var seed = await SeedStoryAsync(UserStoryStatus.InProgress);
        var runId = Guid.NewGuid();

        // Two messages with the same msg-id — simulating a retry after
        // a successful processing that failed to ack in time.
        var consumer = NewConsumer();
        var msg1 = BuildMessage(seed.storyId, runId, "finished");
        await consumer.HandleAsync(msg1, CancellationToken.None);

        var msg2 = new FakeIncomingMessage
        {
            Headers = msg1.Headers,  // same msg-id
            Subject = msg1.Subject,
            Payload = msg1.Payload,
            ReceivedAt = DateTimeOffset.UtcNow
        };
        await consumer.HandleAsync(msg2, CancellationToken.None);

        Assert.True(msg1.Acked);
        Assert.True(msg2.Acked);
        // Both acked. Story state didn't regress (already in InReview).
        await using var verify = new AppDbContext(_options);
        var story = await verify.UserStories.FirstAsync();
        Assert.Equal(UserStoryStatus.InReview, story.Status);
    }

    [Fact]
    public async Task UnknownKind_AcksAndSkips()
    {
        var seed = await SeedStoryAsync(UserStoryStatus.InProgress);
        var runId = Guid.NewGuid();
        var msg = BuildMessage(seed.storyId, runId, "started");  // not a terminal kind

        await NewConsumer().HandleAsync(msg, CancellationToken.None);

        await using var verify = new AppDbContext(_options);
        var story = await verify.UserStories.FirstAsync();
        Assert.Equal(UserStoryStatus.InProgress, story.Status);
        Assert.True(msg.Acked);
    }

    // ── Z2 — IssueId-correlated runs ────────────────────────────────

    [Fact]
    public async Task IssueFinished_WhileTriaging_TransitionsToTriaged()
    {
        var issueId = await SeedIssueAsync(TriageState.Triaging);
        var msg = BuildIssueMessage(issueId, Guid.NewGuid(), "finished");

        await NewConsumer().HandleAsync(msg, CancellationToken.None);

        await using var verify = new AppDbContext(_options);
        var issue = await verify.Issues.FirstAsync(i => i.Id == issueId);
        Assert.Equal(TriageState.Triaged, issue.TriageState);
        Assert.True(msg.Acked);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public async Task IssueFailedOrCancelled_LeavesStateUnchanged(string kind)
    {
        var issueId = await SeedIssueAsync(TriageState.Triaging);
        var msg = BuildIssueMessage(issueId, Guid.NewGuid(), kind);

        await NewConsumer().HandleAsync(msg, CancellationToken.None);

        await using var verify = new AppDbContext(_options);
        var issue = await verify.Issues.FirstAsync(i => i.Id == issueId);
        Assert.Equal(TriageState.Triaging, issue.TriageState);
        Assert.True(msg.Acked);
    }

    [Fact]
    public async Task IssueFinished_NotInTriaging_DoesNotRegress()
    {
        // A premature or replayed event for an issue that's already been
        // triaged (and possibly accepted) must not re-call CompleteTriage.
        var issueId = await SeedIssueAsync(TriageState.Triaged);
        var msg = BuildIssueMessage(issueId, Guid.NewGuid(), "finished");

        await NewConsumer().HandleAsync(msg, CancellationToken.None);

        await using var verify = new AppDbContext(_options);
        var issue = await verify.Issues.FirstAsync(i => i.Id == issueId);
        Assert.Equal(TriageState.Triaged, issue.TriageState);
        Assert.True(msg.Acked);
    }

    [Fact]
    public async Task UnknownIssueId_AcksAndSkips()
    {
        var msg = BuildIssueMessage(Guid.NewGuid(), Guid.NewGuid(), "finished");

        await NewConsumer().HandleAsync(msg, CancellationToken.None);

        Assert.True(msg.Acked);
    }

    [Fact]
    public async Task NeitherStoryNorIssue_AcksAndSkips()
    {
        var msg = BuildMessage(storyId: null, Guid.NewGuid(), "finished");
        // BuildMessage already uses null StoryId and the IssueId default
        // is null too, so the payload has nothing to correlate.

        await NewConsumer().HandleAsync(msg, CancellationToken.None);
        Assert.True(msg.Acked);
    }

    private sealed class NoopTriageEstimator : ITriageEstimator
    {
        public Andy.Issues.Domain.ValueTypes.EstimateSlot Estimate(
            string tenantId,
            Andy.Issues.Domain.Enums.TriageTemplateId templateId,
            Andy.Issues.Domain.Enums.TriageSeverity severity)
            => new();
    }

    private sealed class FakeIncomingMessage : IncomingMessage
    {
        public bool Acked { get; private set; }
        public bool Nacked { get; private set; }

        public override Task AckAsync(CancellationToken ct = default)
        {
            Acked = true;
            return Task.CompletedTask;
        }

        public override Task NackAsync(CancellationToken ct = default)
        {
            Nacked = true;
            return Task.CompletedTask;
        }
    }

    private sealed class DummyBus : IMessageBus
    {
        public Task PublishAsync(string subject, object payload, MessageHeaders headers, CancellationToken ct = default) =>
            Task.CompletedTask;
        public IAsyncEnumerable<IncomingMessage> SubscribeAsync(string subjectFilter, SubscriptionOptions options, CancellationToken ct = default) =>
            throw new NotSupportedException("Not exercised in unit tests.");
    }
}
