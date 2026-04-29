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

    // ── Z5 — Revisions ──────────────────────────────────────────────

    private static Andy.Issues.Domain.ValueTypes.TriageOutput MakeOutput(string rationale = "Repro.") =>
        new(Andy.Issues.Domain.Enums.TriageTemplateId.BugFix,
            Andy.Issues.Domain.Enums.TriageSeverity.Critical,
            "rivoli-ai/andy-tasks", rationale,
            Array.Empty<Andy.Issues.Domain.ValueTypes.DocsRef>(),
            new Andy.Issues.Domain.ValueTypes.EstimateSlot());

    [Fact]
    public async Task CompleteTriage_WithOutput_AppendsAgentRevision()
    {
        var id = await CreateIssueAsync();
        await using (var ctx = NewContext())
            await NewService(ctx).StartTriageAsync(id, "alice");
        await using (var ctx = NewContext())
            await NewService(ctx).CompleteTriageAsync(id, "alice", MakeOutput());

        await using var verify = NewContext();
        var revisions = await verify.TriageOutputRevisions
            .Where(r => r.IssueId == id).ToListAsync();
        Assert.Single(revisions);
        Assert.Equal(Andy.Issues.Domain.Enums.TriageRevisionAuthorKind.Agent,
            revisions[0].AuthorKind);
        Assert.Equal("alice", revisions[0].Author);
    }

    [Fact]
    public async Task EditOutput_AppendsHumanRevisionAndEmitsRevisedEvent()
    {
        var id = await CreateIssueAsync();
        await using (var ctx = NewContext())
            await NewService(ctx).StartTriageAsync(id, "alice");
        await using (var ctx = NewContext())
            await NewService(ctx).CompleteTriageAsync(id, "alice", MakeOutput("First."));

        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).EditOutputAsync(
                id, "alice", MakeOutput("Reclassified."), diffSummary: "Severity bumped.");
            Assert.Equal(IssueTriageOutcome.Updated, result.Outcome);
        }

        await using var verify = NewContext();
        var revisions = await verify.TriageOutputRevisions
            .Where(r => r.IssueId == id).OrderBy(r => r.CreatedAt).ToListAsync();
        Assert.Equal(2, revisions.Count);
        Assert.Equal(Andy.Issues.Domain.Enums.TriageRevisionAuthorKind.Human, revisions[1].AuthorKind);
        Assert.Equal("Severity bumped.", revisions[1].DiffSummary);

        var revisedRows = await verify.Outbox.CountAsync(e => e.Subject.EndsWith(".revised"));
        Assert.Equal(1, revisedRows);
    }

    [Fact]
    public async Task EditOutput_FromNonTriagedState_ReturnsInvalidTransition()
    {
        var id = await CreateIssueAsync();
        // Issue is NeedsTriage — edit not allowed.
        await using var ctx = NewContext();
        var result = await NewService(ctx).EditOutputAsync(id, "alice", MakeOutput());
        Assert.Equal(IssueTriageOutcome.InvalidTransition, result.Outcome);
    }

    [Fact]
    public async Task Revert_ToPriorRevision_AppendsHumanRevisionWithThatContent()
    {
        var id = await CreateIssueAsync();
        await using (var ctx = NewContext())
            await NewService(ctx).StartTriageAsync(id, "alice");
        await using (var ctx = NewContext())
            await NewService(ctx).CompleteTriageAsync(id, "alice", MakeOutput("First."));
        await using (var ctx = NewContext())
            await NewService(ctx).EditOutputAsync(id, "alice", MakeOutput("Second."));

        Guid firstRevisionId;
        await using (var ctx = NewContext())
        {
            firstRevisionId = (await ctx.TriageOutputRevisions
                .Where(r => r.IssueId == id).OrderBy(r => r.CreatedAt).FirstAsync()).Id;
        }

        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).RevertAsync(id, "alice", firstRevisionId);
            Assert.Equal(IssueTriageOutcome.Updated, result.Outcome);
            Assert.Equal("First.", result.Issue!.TriageOutput!.Rationale);
        }

        await using var verify = NewContext();
        // Three revisions total: agent-First, human-Second, human-Reverted.
        var count = await verify.TriageOutputRevisions
            .Where(r => r.IssueId == id).CountAsync();
        Assert.Equal(3, count);

        // Revert emits a `revised` event (same kind as edit).
        var revisedRows = await verify.Outbox.CountAsync(e => e.Subject.EndsWith(".revised"));
        Assert.Equal(2, revisedRows);
    }

    [Fact]
    public async Task Revert_UnknownTargetRevision_ReturnsNotFound()
    {
        var id = await CreateIssueAsync();
        await using (var ctx = NewContext())
            await NewService(ctx).StartTriageAsync(id, "alice");
        await using (var ctx = NewContext())
            await NewService(ctx).CompleteTriageAsync(id, "alice", MakeOutput());

        await using var ctx2 = NewContext();
        var result = await NewService(ctx2).RevertAsync(id, "alice", Guid.NewGuid());
        Assert.Equal(IssueTriageOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task ListRevisions_ReturnsNewestFirst_OwnerScoped()
    {
        var id = await CreateIssueAsync(owner: "alice");
        await using (var ctx = NewContext())
            await NewService(ctx).StartTriageAsync(id, "alice");
        await using (var ctx = NewContext())
            await NewService(ctx).CompleteTriageAsync(id, "alice", MakeOutput("v1"));
        await Task.Delay(10);
        await using (var ctx = NewContext())
            await NewService(ctx).EditOutputAsync(id, "alice", MakeOutput("v2"));

        await using var ctx2 = NewContext();
        var rows = await NewService(ctx2).ListRevisionsAsync(id, "alice");
        Assert.NotNull(rows);
        Assert.Equal(2, rows!.Count);
        Assert.Equal("v2", rows[0].TriageOutput.Rationale); // newest first
        Assert.Equal("v1", rows[1].TriageOutput.Rationale);

        // Different owner — null.
        var fromBob = await NewService(ctx2).ListRevisionsAsync(id, "bob");
        Assert.Null(fromBob);
    }
}
