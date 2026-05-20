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

// #187 — IssueService.ListIssuesAsync against an in-memory SQLite so
// EF mappings (string-converted enum, indexes, AssigneeUserId column)
// are exercised. One test per filter axis + cursor pagination so a
// regression points at the broken filter, not at "the list endpoint".
public class IssueServiceListIssuesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public IssueServiceListIssuesTests()
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
    private IssueService NewService(AppDbContext ctx) =>
        new(ctx, new TestDocsClient(), new BacklogSequenceAllocator(ctx));

    private static Andy.Issues.Domain.ValueTypes.TriageOutput MakeOutput() =>
        new(
            Andy.Issues.Domain.Enums.TriageTemplateId.BugFix,
            Andy.Issues.Domain.Enums.TriageSeverity.Critical,
            null,
            "rationale",
            Array.Empty<Andy.Issues.Domain.ValueTypes.DocsRef>(),
            new Andy.Issues.Domain.ValueTypes.EstimateSlot());

    private async Task<Issue> SeedIssueAsync(
        string owner,
        string title,
        TriageState state = TriageState.NeedsTriage,
        string? assignee = null,
        Guid? repository = null,
        DateTimeOffset? createdAt = null)
    {
        // Skip the service's CreateAsync path so we can seed terminal
        // states + arbitrary timestamps directly. We still go through
        // BacklogSequenceAllocator so Seq is consistent.
        await using var ctx = NewContext();
        var seq = await new BacklogSequenceAllocator(ctx)
            .AllocateAsync(BacklogEntityType.Issue);

        var issue = new Issue
        {
            Id = Guid.NewGuid(),
            Seq = seq,
            OwnerUserId = owner,
            AssigneeUserId = assignee,
            RepositoryId = repository,
            Title = title,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };

        // Drive the requested terminal state via the entity transitions
        // so invariants stay satisfied. Order: Triaging → Triaged →
        // Accepted/Rejected.
        switch (state)
        {
            case TriageState.NeedsTriage:
                break;
            case TriageState.Triaging:
                issue.StartTriage();
                break;
            case TriageState.Triaged:
                issue.StartTriage();
                issue.CompleteTriage(owner, MakeOutput());
                break;
            case TriageState.Accepted:
                issue.StartTriage();
                issue.CompleteTriage(owner, MakeOutput());
                issue.Accept(owner);
                break;
            case TriageState.Rejected:
                issue.StartTriage();
                issue.CompleteTriage(owner, MakeOutput());
                issue.Reject(owner);
                break;
        }

        ctx.Issues.Add(issue);
        await ctx.SaveChangesAsync();
        return issue;
    }

    private IssueListQuery Query(
        IReadOnlyList<TriageState>? states = null,
        AssigneeFilter? assignee = null,
        Guid? repository = null,
        int limit = 50,
        string? cursor = null) =>
        new(states, assignee ?? AssigneeFilter.Unfiltered, repository, limit, cursor);

    [Fact]
    public async Task ListIssuesAsync_NoFilters_ReturnsAllOwnerIssues()
    {
        await SeedIssueAsync("alice", "a1");
        await SeedIssueAsync("alice", "a2");
        await SeedIssueAsync("bob", "b1");

        await using var ctx = NewContext();
        var result = await NewService(ctx).ListIssuesAsync("alice", Query());

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.Equal("alice", i.OwnerUserId));
    }

    [Fact]
    public async Task ListIssuesAsync_StateFilter_MatchesAnyOf()
    {
        await SeedIssueAsync("alice", "needs", TriageState.NeedsTriage);
        await SeedIssueAsync("alice", "triaging", TriageState.Triaging);
        await SeedIssueAsync("alice", "triaged", TriageState.Triaged);
        await SeedIssueAsync("alice", "accepted", TriageState.Accepted);

        await using var ctx = NewContext();
        var result = await NewService(ctx).ListIssuesAsync(
            "alice",
            Query(states: new[]
            {
                TriageState.NeedsTriage, TriageState.Triaging, TriageState.Triaged
            }));

        // AF2 pipeline-kanban slice: three lifecycle states OR'd.
        Assert.Equal(3, result.Items.Count);
        Assert.DoesNotContain(result.Items, i => i.Title == "accepted");
    }

    [Fact]
    public async Task ListIssuesAsync_AssigneeNone_ReturnsUnassignedOnly()
    {
        await SeedIssueAsync("alice", "unassigned-a", assignee: null);
        await SeedIssueAsync("alice", "unassigned-b", assignee: null);
        await SeedIssueAsync("alice", "assigned-to-bob", assignee: "bob");

        await using var ctx = NewContext();
        var result = await NewService(ctx).ListIssuesAsync(
            "alice", Query(assignee: AssigneeFilter.None));

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.Null(i.AssigneeUserId));
    }

    [Fact]
    public async Task ListIssuesAsync_AssigneeSpecificUser_FiltersToThatUser()
    {
        await SeedIssueAsync("alice", "to-bob", assignee: "bob");
        await SeedIssueAsync("alice", "to-carol", assignee: "carol");
        await SeedIssueAsync("alice", "unassigned", assignee: null);

        await using var ctx = NewContext();
        var result = await NewService(ctx).ListIssuesAsync(
            "alice", Query(assignee: AssigneeFilter.ForUser("bob")));

        var only = Assert.Single(result.Items);
        Assert.Equal("to-bob", only.Title);
        Assert.Equal("bob", only.AssigneeUserId);
    }

    [Fact]
    public async Task ListIssuesAsync_RepositoryFilter_ScopesToRepo()
    {
        var repoA = Guid.NewGuid();
        var repoB = Guid.NewGuid();
        await SeedIssueAsync("alice", "a-in-a", repository: repoA);
        await SeedIssueAsync("alice", "b-in-b", repository: repoB);
        await SeedIssueAsync("alice", "c-no-repo", repository: null);

        await using var ctx = NewContext();
        var result = await NewService(ctx).ListIssuesAsync(
            "alice", Query(repository: repoA));

        var only = Assert.Single(result.Items);
        Assert.Equal("a-in-a", only.Title);
    }

    [Fact]
    public async Task ListIssuesAsync_Af3IntakeSlice_StateNeedsTriagePlusAssigneeNone()
    {
        // Pin the exact AF3 query: state=needs-triage + assignee=none.
        // This is the conductor cockpit intake pane's default queue.
        await SeedIssueAsync("alice", "intake-1", TriageState.NeedsTriage, assignee: null);
        await SeedIssueAsync("alice", "intake-2", TriageState.NeedsTriage, assignee: null);
        await SeedIssueAsync("alice", "claimed", TriageState.NeedsTriage, assignee: "bob");
        await SeedIssueAsync("alice", "in-flight", TriageState.Triaging, assignee: null);

        await using var ctx = NewContext();
        var result = await NewService(ctx).ListIssuesAsync(
            "alice",
            Query(
                states: new[] { TriageState.NeedsTriage },
                assignee: AssigneeFilter.None));

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i =>
        {
            Assert.Equal("NeedsTriage", i.TriageState);
            Assert.Null(i.AssigneeUserId);
        });
    }

    [Fact]
    public async Task ListIssuesAsync_CursorPaginates_AndTerminatesWithNullCursor()
    {
        // Seed five issues with monotonically increasing CreatedAt so
        // the (CreatedAt DESC, Id DESC) order is well-defined.
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        for (var i = 0; i < 5; i++)
        {
            await SeedIssueAsync("alice", $"i{i}", createdAt: t0.AddSeconds(i));
        }

        await using var ctx = NewContext();
        var svc = NewService(ctx);

        var page1 = await svc.ListIssuesAsync("alice", Query(limit: 2));
        Assert.Equal(2, page1.Items.Count);
        Assert.NotNull(page1.Cursor);

        var page2 = await svc.ListIssuesAsync("alice", Query(limit: 2, cursor: page1.Cursor));
        Assert.Equal(2, page2.Items.Count);
        Assert.NotNull(page2.Cursor);
        Assert.Empty(page1.Items.Select(i => i.Id).Intersect(page2.Items.Select(i => i.Id)));

        var page3 = await svc.ListIssuesAsync("alice", Query(limit: 2, cursor: page2.Cursor));
        Assert.Single(page3.Items);
        Assert.Null(page3.Cursor);
    }

    [Fact]
    public async Task ListIssuesAsync_DoesNotLeakAcrossOwners()
    {
        await SeedIssueAsync("alice", "mine");
        await SeedIssueAsync("bob", "not-mine");

        await using var ctx = NewContext();
        var result = await NewService(ctx).ListIssuesAsync("alice", Query());

        var only = Assert.Single(result.Items);
        Assert.Equal("mine", only.Title);
    }
}

// File-local IDocsClient stub mirroring the one in IssueServiceTests
// (file-class). Decoupled from the shared service tests so this file
// builds in isolation if those tests are restructured.
file class TestDocsClient : IDocsClient
{
    public Task<bool> VerifyLinkAsync(Guid linkId, string expectedTargetType, Guid expectedTargetId, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<DocsMetadata?> GetMetadataAsync(Guid documentId, CancellationToken ct = default) =>
        Task.FromResult<DocsMetadata?>(null);
}
