// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Enums;
using Xunit;

namespace Andy.Issues.Tests.Unit.Requests;

// #187 — parser unit tests for the unified `GET /api/issues`
// query-string contract. Each test pins one piece of the wire shape
// in isolation so a regression points at the broken filter, not at
// "the list endpoint".
public class IssueListQueryParserTests
{
    [Fact]
    public void TryParse_NoFilters_ReturnsDefaults()
    {
        var ok = IssueListQueryParser.TryParse(
            state: null, assignee: null, authenticatedUserId: "alice",
            repository: null, limit: null, cursor: null,
            out var query, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Null(query.States);
        Assert.Equal(AssigneeFilterKind.Unfiltered, query.Assignee.Kind);
        Assert.Null(query.RepositoryId);
        Assert.Equal(IssueListQueryParser.DefaultLimit, query.Limit);
        Assert.Null(query.Cursor);
    }

    [Fact]
    public void TryParse_SingleHyphenatedState_AcceptsWireForm()
    {
        var ok = IssueListQueryParser.TryParse(
            state: "needs-triage", assignee: null, authenticatedUserId: "alice",
            repository: null, limit: null, cursor: null,
            out var query, out _);

        Assert.True(ok);
        Assert.NotNull(query.States);
        Assert.Single(query.States!);
        Assert.Equal(TriageState.NeedsTriage, query.States![0]);
    }

    [Fact]
    public void TryParse_PascalCaseState_AcceptedForLegacyParity()
    {
        // The MCP/CLI surface has historically accepted the PascalCase
        // enum name. The unified endpoint accepts both forms so the
        // same client method works regardless of which casing the
        // upstream code generator chose.
        var ok = IssueListQueryParser.TryParse(
            state: "NeedsTriage", assignee: null, authenticatedUserId: "alice",
            repository: null, limit: null, cursor: null,
            out var query, out _);

        Assert.True(ok);
        Assert.Equal(TriageState.NeedsTriage, query.States![0]);
    }

    [Fact]
    public void TryParse_CsvStates_MatchesAnyOf()
    {
        var ok = IssueListQueryParser.TryParse(
            state: "needs-triage,triaging,triaged",
            assignee: null, authenticatedUserId: "alice",
            repository: null, limit: null, cursor: null,
            out var query, out _);

        Assert.True(ok);
        Assert.Equal(3, query.States!.Count);
        Assert.Contains(TriageState.NeedsTriage, query.States);
        Assert.Contains(TriageState.Triaging, query.States);
        Assert.Contains(TriageState.Triaged, query.States);
    }

    [Fact]
    public void TryParse_CsvStates_DeduplicatesRepeats()
    {
        var ok = IssueListQueryParser.TryParse(
            state: "needs-triage,needs-triage,triaging",
            assignee: null, authenticatedUserId: "alice",
            repository: null, limit: null, cursor: null,
            out var query, out _);

        Assert.True(ok);
        Assert.Equal(2, query.States!.Count);
    }

    [Fact]
    public void TryParse_UnknownState_400s()
    {
        var ok = IssueListQueryParser.TryParse(
            state: "burning",
            assignee: null, authenticatedUserId: "alice",
            repository: null, limit: null, cursor: null,
            out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("burning", error);
    }

    [Fact]
    public void TryParse_AssigneeNone_MapsToNoneKind()
    {
        var ok = IssueListQueryParser.TryParse(
            state: null, assignee: "none", authenticatedUserId: "alice",
            repository: null, limit: null, cursor: null,
            out var query, out _);

        Assert.True(ok);
        Assert.Equal(AssigneeFilterKind.None, query.Assignee.Kind);
        Assert.Null(query.Assignee.UserId);
    }

    [Fact]
    public void TryParse_AssigneeMe_ResolvesToAuthenticatedPrincipal()
    {
        var ok = IssueListQueryParser.TryParse(
            state: null, assignee: "me", authenticatedUserId: "alice",
            repository: null, limit: null, cursor: null,
            out var query, out _);

        Assert.True(ok);
        Assert.Equal(AssigneeFilterKind.SpecificUser, query.Assignee.Kind);
        Assert.Equal("alice", query.Assignee.UserId);
    }

    [Fact]
    public void TryParse_AssigneeMe_WithoutPrincipal_400s()
    {
        var ok = IssueListQueryParser.TryParse(
            state: null, assignee: "me", authenticatedUserId: null,
            repository: null, limit: null, cursor: null,
            out _, out var error);

        Assert.False(ok);
        Assert.Contains("authenticated", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParse_AssigneeLiteralUserId_PassedThrough()
    {
        var ok = IssueListQueryParser.TryParse(
            state: null, assignee: "bob",
            authenticatedUserId: "alice",
            repository: null, limit: null, cursor: null,
            out var query, out _);

        Assert.True(ok);
        Assert.Equal(AssigneeFilterKind.SpecificUser, query.Assignee.Kind);
        Assert.Equal("bob", query.Assignee.UserId);
    }

    [Fact]
    public void TryParse_LimitClampedToMax()
    {
        var ok = IssueListQueryParser.TryParse(
            state: null, assignee: null, authenticatedUserId: "alice",
            repository: null, limit: 9999, cursor: null,
            out var query, out _);

        Assert.True(ok);
        Assert.Equal(IssueListQueryParser.MaxLimit, query.Limit);
    }

    [Fact]
    public void TryParse_LimitClampedToOne()
    {
        var ok = IssueListQueryParser.TryParse(
            state: null, assignee: null, authenticatedUserId: "alice",
            repository: null, limit: 0, cursor: null,
            out var query, out _);

        Assert.True(ok);
        Assert.Equal(1, query.Limit);
    }

    [Fact]
    public void TryParse_RepositoryAndCursor_PassedThrough()
    {
        var repo = Guid.NewGuid();
        var ok = IssueListQueryParser.TryParse(
            state: null, assignee: null, authenticatedUserId: "alice",
            repository: repo, limit: 25, cursor: "OPAQUE",
            out var query, out _);

        Assert.True(ok);
        Assert.Equal(repo, query.RepositoryId);
        Assert.Equal(25, query.Limit);
        Assert.Equal("OPAQUE", query.Cursor);
    }
}
