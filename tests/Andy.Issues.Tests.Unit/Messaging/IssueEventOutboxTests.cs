// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Messaging;

// Z4 — focused tests for the IssueEventOutbox helper. The Z1 service
// tests already cover the integrated flow (terminal transitions emit
// the right subject + payload via this helper); these tests pin the
// causation/generation chain that ADR-0001 §5 requires when an event
// is published in response to a parent message.
public class IssueEventOutboxTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public IssueEventOutboxTests()
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

    private static Issue MakeTriagedIssue()
    {
        var issue = new Issue { Id = Guid.NewGuid(), OwnerUserId = "alice", Title = "x" };
        issue.StartTriage();
        issue.CompleteTriage("alice");
        return issue;
    }

    [Fact]
    public async Task AppendIssueEvent_NoCausation_DefaultsToRootGeneration()
    {
        await using var ctx = NewContext();
        var issue = MakeTriagedIssue();
        ctx.Issues.Add(issue);

        ctx.AppendIssueEvent(issue, IssueEventKind.Triaged);
        await ctx.SaveChangesAsync();

        var entry = await ctx.Outbox.SingleAsync();
        Assert.Null(entry.CausationId);
        Assert.Equal(0, entry.Generation);
        Assert.Equal(issue.Id, entry.CorrelationId);
    }

    [Fact]
    public async Task AppendIssueEvent_WithCausation_PropagatesAndIncrementsGeneration()
    {
        await using var ctx = NewContext();
        var issue = MakeTriagedIssue();
        ctx.Issues.Add(issue);

        var parentMsgId = Guid.NewGuid();
        const int parentGeneration = 2;

        ctx.AppendIssueEvent(issue, IssueEventKind.Triaged,
            causationId: parentMsgId,
            parentGeneration: parentGeneration);
        await ctx.SaveChangesAsync();

        var entry = await ctx.Outbox.SingleAsync();
        Assert.Equal(parentMsgId, entry.CausationId);

        // ADR-0001 §5: child generation = parent + 1.
        Assert.Equal(parentGeneration + 1, entry.Generation);
        Assert.Equal(issue.Id, entry.CorrelationId);
    }

    [Fact]
    public async Task AppendIssueEvent_SubjectFormat_MatchesTaxonomy()
    {
        await using var ctx = NewContext();
        var issue = MakeTriagedIssue();
        ctx.Issues.Add(issue);

        ctx.AppendIssueEvent(issue, IssueEventKind.Triaged);
        await ctx.SaveChangesAsync();

        var entry = await ctx.Outbox.SingleAsync();
        Assert.Equal($"andy.issues.events.issue.{issue.Id}.triaged", entry.Subject);
        Assert.Equal(typeof(IssueEventPayload).FullName, entry.PayloadType);
    }
}
