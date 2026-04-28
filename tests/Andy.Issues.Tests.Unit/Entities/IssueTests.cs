// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Xunit;

namespace Andy.Issues.Tests.Unit.Entities;

// Exhaustive state-machine table for Issue (Z1). Mirrors the diagram in
// docs/architecture.md and Issue.cs. Every valid transition + every
// invalid transition is asserted.
public class IssueTests
{
    [Fact]
    public void NewIssue_StartsInNeedsTriage()
    {
        var issue = new Issue { Title = "x" };
        Assert.Equal(TriageState.NeedsTriage, issue.TriageState);
    }

    [Fact]
    public void StartTriage_FromNeedsTriage_MovesToTriaging()
    {
        var issue = new Issue { Title = "x" };
        issue.StartTriage();
        Assert.Equal(TriageState.Triaging, issue.TriageState);
        Assert.NotNull(issue.UpdatedAt);
    }

    [Fact]
    public void StartTriage_FromTriaged_ReInvokesTriaging()
    {
        var issue = Triaged();
        issue.StartTriage();
        Assert.Equal(TriageState.Triaging, issue.TriageState);
    }

    [Theory]
    [InlineData(TriageState.Triaging)]
    [InlineData(TriageState.Accepted)]
    [InlineData(TriageState.Rejected)]
    public void StartTriage_FromInvalidState_Throws(TriageState start)
    {
        var issue = AtState(start);
        Assert.Throws<InvalidOperationException>(() => issue.StartTriage());
    }

    [Fact]
    public void CompleteTriage_FromTriaging_MovesToTriaged()
    {
        var issue = new Issue { Title = "x" };
        issue.StartTriage();
        issue.CompleteTriage("agent-1");
        Assert.Equal(TriageState.Triaged, issue.TriageState);
        Assert.Equal("agent-1", issue.TriagedBy);
        Assert.NotNull(issue.TriagedAt);
    }

    [Theory]
    [InlineData(TriageState.NeedsTriage)]
    [InlineData(TriageState.Triaged)]
    [InlineData(TriageState.Accepted)]
    [InlineData(TriageState.Rejected)]
    public void CompleteTriage_FromInvalidState_Throws(TriageState start)
    {
        var issue = AtState(start);
        Assert.Throws<InvalidOperationException>(() => issue.CompleteTriage("agent-1"));
    }

    [Fact]
    public void Accept_FromTriaged_MovesToAccepted()
    {
        var issue = Triaged();
        issue.Accept("alice");
        Assert.Equal(TriageState.Accepted, issue.TriageState);
        Assert.Equal("alice", issue.TriagedBy);
    }

    [Fact]
    public void Accept_OnAlreadyAccepted_IsNoOp()
    {
        var issue = Triaged();
        issue.Accept("alice");
        var firstUpdate = issue.UpdatedAt;

        // Repeated accept must succeed (idempotency per spec).
        issue.Accept("alice");
        Assert.Equal(TriageState.Accepted, issue.TriageState);
        Assert.NotNull(issue.UpdatedAt);
        Assert.True(issue.UpdatedAt >= firstUpdate);
    }

    [Theory]
    [InlineData(TriageState.NeedsTriage)]
    [InlineData(TriageState.Triaging)]
    [InlineData(TriageState.Rejected)]
    public void Accept_FromInvalidState_Throws(TriageState start)
    {
        var issue = AtState(start);
        Assert.Throws<InvalidOperationException>(() => issue.Accept("alice"));
    }

    [Fact]
    public void Reject_FromTriaged_MovesToRejected()
    {
        var issue = Triaged();
        issue.Reject("alice");
        Assert.Equal(TriageState.Rejected, issue.TriageState);
    }

    [Fact]
    public void Reject_OnAlreadyRejected_IsNoOp()
    {
        var issue = Triaged();
        issue.Reject("alice");

        issue.Reject("alice");
        Assert.Equal(TriageState.Rejected, issue.TriageState);
    }

    [Theory]
    [InlineData(TriageState.NeedsTriage)]
    [InlineData(TriageState.Triaging)]
    [InlineData(TriageState.Accepted)]
    public void Reject_FromInvalidState_Throws(TriageState start)
    {
        var issue = AtState(start);
        Assert.Throws<InvalidOperationException>(() => issue.Reject("alice"));
    }

    [Fact]
    public void TerminalAccepted_CannotBeRejected()
    {
        var issue = Triaged();
        issue.Accept("alice");
        Assert.Throws<InvalidOperationException>(() => issue.Reject("alice"));
    }

    [Fact]
    public void TerminalRejected_CannotBeAccepted()
    {
        var issue = Triaged();
        issue.Reject("alice");
        Assert.Throws<InvalidOperationException>(() => issue.Accept("alice"));
    }

    private static Issue Triaged()
    {
        var issue = new Issue { Title = "x" };
        issue.StartTriage();
        issue.CompleteTriage("agent-1");
        return issue;
    }

    private static Issue AtState(TriageState state)
    {
        var issue = new Issue { Title = "x" };
        switch (state)
        {
            case TriageState.NeedsTriage:
                break;
            case TriageState.Triaging:
                issue.StartTriage();
                break;
            case TriageState.Triaged:
                issue.StartTriage();
                issue.CompleteTriage("agent-1");
                break;
            case TriageState.Accepted:
                issue.StartTriage();
                issue.CompleteTriage("agent-1");
                issue.Accept("alice");
                break;
            case TriageState.Rejected:
                issue.StartTriage();
                issue.CompleteTriage("agent-1");
                issue.Reject("alice");
                break;
        }
        return issue;
    }
}
