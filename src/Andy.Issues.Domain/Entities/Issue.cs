// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.ValueTypes;

namespace Andy.Issues.Domain.Entities;

// Triage-tracked issue (Epic Z, Z1). Distinct from UserStory: an Issue
// is the intake envelope that the triage agent classifies before any
// backlog item is produced. The state machine is enforced here so that
// every caller — REST controller, MCP tool, CLI — goes through the same
// invariants.
public class Issue
{
    public Guid Id { get; set; }

    /// <summary>
    /// AH6 (rivoli-ai/conductor#713) — monotonic sequence allocated
    /// by <see cref="IBacklogSequenceAllocator"/> before insert.
    /// Projected as <see cref="DisplayId"/>. Immutable once stamped.
    /// </summary>
    public long Seq { get; internal set; }

    /// <summary>
    /// AH6 — human-readable short identifier (<c>ISSUE-99</c>) derived
    /// from <see cref="Seq"/>. Carried on the
    /// <c>andy.issues.events.issue.*.triaged</c> payload so andy-tasks
    /// can pin it on the resulting Goal and emit it back on
    /// <c>GoalCreatedEvent.SourceIssueDisplayId</c>.
    /// </summary>
    public string DisplayId => $"ISSUE-{Seq}";

    /// <summary>
    /// AH6 — reverse linkage written by the
    /// <c>GoalLinkageConsumer</c> when andy-tasks emits
    /// <c>andy.tasks.events.goal.{id}.created</c> with this issue's
    /// display id pinned as <c>SourceIssueDisplayId</c>. Stable
    /// <c>GOAL-{n}</c> form. Null while no goal references this
    /// issue (manual issue creation, or pre-AH6 emissions that
    /// didn't carry the back-pin).
    /// </summary>
    public string? GoalDisplayId { get; set; }

    // Owner (the user who filed the issue). Mirrors the OwnerUserId
    // pattern used by Repository / Sandbox; there is no tenant axis in
    // this codebase yet.
    public string OwnerUserId { get; set; } = string.Empty;

    // #187 — assignee on the triage envelope. Distinct from
    // <see cref="OwnerUserId"/> (the filer) and from
    // <see cref="TriagedBy"/> (the last actor on a transition). Null
    // when the issue is unassigned (the AF3 cockpit intake pane's
    // default queue). The unified `GET /api/issues` endpoint filters
    // on this column with `assignee=none|me|<user-id>`.
    public string? AssigneeUserId { get; set; }

    // Optional repository the issue is filed against. Triage may also
    // suggest a target repo (Z3) — that is a separate field on the
    // triage output, not this attribution.
    public Guid? RepositoryId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }

    public TriageState TriageState { get; private set; } = TriageState.NeedsTriage;

    // Z2 — id of the headless agent run dispatched to andy-containers
    // to perform triage. Set when StartTriage is invoked alongside an
    // agent dispatch; null when triage has not yet been kicked off, or
    // when the issue was triaged manually (no agent run). Used by the
    // run-event consumer for IssueId-correlated runs and by Z6 audit
    // queries.
    public Guid? TriageRunId { get; set; }

    // Set when triage produces output (state moves to Triaged) and when
    // a human accepts/rejects. Captures who acted and when, distinct
    // from the agent run audit log (Z6).
    public DateTimeOffset? TriagedAt { get; private set; }
    public string? TriagedBy { get; private set; }

    // Z3 — agent-produced classification. Null until CompleteTriage is
    // called with an output payload; persisted as JSON on the row.
    // Stored via the EF backing field below; consumers see only the
    // typed property.
    public TriageOutput? TriageOutput { get; private set; }

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

    public void CompleteTriage(string triagedBy, TriageOutput? output = null)
    {
        if (TriageState != TriageState.Triaging)
            throw new InvalidOperationException(
                $"Invalid triage transition: {TriageState} → Triaged.");

        if (output is not null)
        {
            // Rationale is the only required free-form field; templates
            // and severity are enums and cannot be silently empty.
            if (string.IsNullOrWhiteSpace(output.Rationale))
                throw new ArgumentException(
                    "TriageOutput.Rationale must be non-empty.", nameof(output));
        }

        TriageState = TriageState.Triaged;
        TriagedAt = DateTimeOffset.UtcNow;
        TriagedBy = triagedBy;
        TriageOutput = output;
        UpdatedAt = TriagedAt;
    }

    // Z5 — human edit of the latest triage output. Allowed only while
    // Triaged (not while the agent is still running, not after a
    // terminal accept/reject). The service is responsible for
    // appending a TriageOutputRevision row for this change; the
    // entity just enforces the transition constraint and validation.
    public void EditOutput(TriageOutput output, string editedBy)
    {
        if (TriageState != TriageState.Triaged)
            throw new InvalidOperationException(
                $"Cannot edit triage output while in {TriageState}; allowed only in Triaged.");

        if (string.IsNullOrWhiteSpace(output.Rationale))
            throw new ArgumentException(
                "TriageOutput.Rationale must be non-empty.", nameof(output));

        TriageOutput = output;
        TriagedBy = editedBy;
        UpdatedAt = DateTimeOffset.UtcNow;
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
