// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

// IssueService end-to-end against an in-memory SQLite (so EF mappings,
// outbox, and string-converted enums are all exercised). Verifies that
// every terminal transition appends exactly one outbox row with the
// right subject, and that idempotent terminal calls do not duplicate.
public class IssueServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public IssueServiceTests()
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
    private IssueService NewService(AppDbContext ctx) => new(ctx);

    private async Task<Guid> CreateIssueAsync(string owner = "alice")
    {
        await using var ctx = NewContext();
        var dto = await NewService(ctx).CreateAsync(
            new CreateIssueRequest("intake", "body", null), owner);
        return dto.Id;
    }

    [Fact]
    public async Task Create_StartsInNeedsTriage()
    {
        var id = await CreateIssueAsync();

        await using var verify = NewContext();
        var issue = await verify.Issues.SingleAsync(i => i.Id == id);
        Assert.Equal(TriageState.NeedsTriage, issue.TriageState);
        Assert.Empty(verify.Outbox);
    }

    [Fact]
    public async Task StartTriage_DoesNotEmitOutboxRow()
    {
        var id = await CreateIssueAsync();
        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).StartTriageAsync(id, "alice");
            Assert.Equal(IssueTriageOutcome.Updated, result.Outcome);
        }

        await using var verify = NewContext();
        Assert.Empty(verify.Outbox);
    }

    [Fact]
    public async Task CompleteTriage_EmitsTriagedEvent()
    {
        var id = await CreateIssueAsync();
        await using (var ctx = NewContext())
            await NewService(ctx).StartTriageAsync(id, "alice");

        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).CompleteTriageAsync(id, "alice");
            Assert.Equal(IssueTriageOutcome.Updated, result.Outcome);
        }

        await using var verify = NewContext();
        var entry = await verify.Outbox.SingleAsync();
        Assert.Equal($"andy.issues.events.issue.{id}.triaged", entry.Subject);

        using var doc = JsonDocument.Parse(entry.PayloadJson);
        var root = doc.RootElement;
        Assert.Equal(id.ToString(), root.GetProperty("issue_id").GetString());
        Assert.Equal("Triaged", root.GetProperty("triage_state").GetString());
        Assert.Equal(IssueEventPayload.SchemaVersion,
            root.GetProperty("schema_version").GetInt32());
    }

    [Fact]
    public async Task Accept_FromTriaged_EmitsAcceptedEvent()
    {
        var id = await CreateIssueAsync();
        await using (var ctx = NewContext())
            await NewService(ctx).StartTriageAsync(id, "alice");
        await using (var ctx = NewContext())
            await NewService(ctx).CompleteTriageAsync(id, "alice");

        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).AcceptAsync(id, "alice");
            Assert.Equal(IssueTriageOutcome.Updated, result.Outcome);
        }

        await using var verify = NewContext();
        var subjects = await verify.Outbox.OrderBy(e => e.CreatedAt)
            .Select(e => e.Subject).ToListAsync();
        Assert.Equal(2, subjects.Count);
        Assert.EndsWith(".triaged", subjects[0]);
        Assert.EndsWith(".accepted", subjects[1]);
    }

    [Fact]
    public async Task Reject_FromTriaged_EmitsRejectedEvent()
    {
        var id = await CreateIssueAsync();
        await using (var ctx = NewContext())
            await NewService(ctx).StartTriageAsync(id, "alice");
        await using (var ctx = NewContext())
            await NewService(ctx).CompleteTriageAsync(id, "alice");
        await using (var ctx = NewContext())
            await NewService(ctx).RejectAsync(id, "alice");

        await using var verify = NewContext();
        var last = await verify.Outbox.OrderByDescending(e => e.CreatedAt).FirstAsync();
        Assert.EndsWith(".rejected", last.Subject);
    }

    [Fact]
    public async Task RepeatedAccept_DoesNotDuplicateOutboxRow()
    {
        var id = await CreateIssueAsync();
        await using (var ctx = NewContext())
            await NewService(ctx).StartTriageAsync(id, "alice");
        await using (var ctx = NewContext())
            await NewService(ctx).CompleteTriageAsync(id, "alice");
        await using (var ctx = NewContext())
            await NewService(ctx).AcceptAsync(id, "alice");

        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).AcceptAsync(id, "alice");
            Assert.Equal(IssueTriageOutcome.Updated, result.Outcome);
        }

        await using var verify = NewContext();
        var acceptedRows = await verify.Outbox.CountAsync(e => e.Subject.EndsWith(".accepted"));
        Assert.Equal(1, acceptedRows);
    }

    [Fact]
    public async Task InvalidTransition_ReturnsInvalidTransition()
    {
        var id = await CreateIssueAsync();
        await using var ctx = NewContext();
        var result = await NewService(ctx).AcceptAsync(id, "alice");
        Assert.Equal(IssueTriageOutcome.InvalidTransition, result.Outcome);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Transition_ByDifferentOwner_ReturnsNotFound()
    {
        var id = await CreateIssueAsync(owner: "alice");
        await using var ctx = NewContext();
        var result = await NewService(ctx).StartTriageAsync(id, "bob");
        Assert.Equal(IssueTriageOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Get_ReturnsNullForOtherOwner()
    {
        var id = await CreateIssueAsync(owner: "alice");
        await using var ctx = NewContext();
        var dto = await NewService(ctx).GetAsync(id, "bob");
        Assert.Null(dto);
    }
}
