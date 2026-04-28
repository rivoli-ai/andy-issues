// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Domain.Entities;

// Triage-tracked issue (Epic Z, Z1). Distinct from UserStory: an Issue
// is the intake envelope that the triage agent classifies before any
// backlog item is produced. The state machine is enforced here so that
// every caller — REST controller, MCP tool, CLI — goes through the same
// invariants.
public class Issue
{
    public Guid Id { get; set; }

    // Owner (the user who filed the issue). Mirrors the OwnerUserId
    // pattern used by Repository / Sandbox; there is no tenant axis in
    // this codebase yet.
    public string OwnerUserId { get; set; } = string.Empty;

    // Optional repository the issue is filed against. Triage may also
    // suggest a target repo (Z3) — that is a separate field on the
    // triage output, not this attribution.
    public Guid? RepositoryId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }

    public TriageState TriageState { get; private set; } = TriageState.NeedsTriage;

    // Set when triage produces output (state moves to Triaged) and when
    // a human accepts/rejects. Captures who acted and when, distinct
    // from the agent run audit log (Z6).
    public DateTimeOffset? TriagedAt { get; private set; }
    public string? TriagedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; private set; }

    // State machine — single source of truth, mirrored in unit tests.
    //   NeedsTriage → Triaging        (StartTriage; agent invocation Z2)
    //   Triaging    → Triaged         (CompleteTriage; output produced Z3)
    //   Triaged     → Triaging        (re-invoke via Z9/Z10)
    //   Triaged     → Accepted        (human approves on Conductor Z5)
    //   Triaged     → Rejected        (human rejects on Conductor Z5)
    //   Accepted, Rejected            terminal
    //
    // Idempotency: the no-op transitions (Accepted → Accepted,
    // Rejected → Rejected) match UserStory.SetStatus semantics — the
    // call succeeds, UpdatedAt is bumped, and (per IssueService) no
    // duplicate outbox row is appended.

    public void StartTriage()
    {
        if (TriageState is not (TriageState.NeedsTriage or TriageState.Triaged))
            throw new InvalidOperationException(
                $"Invalid triage transition: {TriageState} → Triaging.");

        TriageState = TriageState.Triaging;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void CompleteTriage(string triagedBy)
    {
        if (TriageState != TriageState.Triaging)
            throw new InvalidOperationException(
                $"Invalid triage transition: {TriageState} → Triaged.");

        TriageState = TriageState.Triaged;
        TriagedAt = DateTimeOffset.UtcNow;
        TriagedBy = triagedBy;
        UpdatedAt = TriagedAt;
    }

    public void Accept(string acceptedBy)
    {
        if (TriageState == TriageState.Accepted)
        {
            UpdatedAt = DateTimeOffset.UtcNow;
            return;
        }

        if (TriageState != TriageState.Triaged)
            throw new InvalidOperationException(
                $"Invalid triage transition: {TriageState} → Accepted.");

        TriageState = TriageState.Accepted;
        TriagedBy = acceptedBy;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Reject(string rejectedBy)
    {
        if (TriageState == TriageState.Rejected)
        {
            UpdatedAt = DateTimeOffset.UtcNow;
            return;
        }

        if (TriageState != TriageState.Triaged)
            throw new InvalidOperationException(
                $"Invalid triage transition: {TriageState} → Rejected.");

        TriageState = TriageState.Rejected;
        TriagedBy = rejectedBy;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
