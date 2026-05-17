// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.ValueTypes;

namespace Andy.Issues.Application.Messaging.Events;

// Payload for andy.issues.events.issue.{issueId}.{kind} events per
// ADR 0001 §2 (Z1) + Z3 + Z4.
//
// Z3 — `TriageOutput` carries the full agent-produced classification
// when the issue reaches `Triaged`/`Accepted`/`Rejected`. Null on
// payloads emitted before an output was attached (e.g. a manual
// CompleteTriage during Z1 testing). Schema stays at v1 — the
// existing fields are unchanged; the new field is additive and
// optional, so consumers built against the Z1 envelope deserialize
// cleanly.
//
// Z4 — this type IS what the Z4 spec calls `IssueTriagedPayload`.
// The codebase keeps the broader name (`IssueEventPayload`) because
// the same shape is reused for accepted/rejected subjects, mirroring
// `StoryEventPayload`'s convention. The wire schema for the
// `triaged` subject specifically is published in
// schemas/triage-output.v1.json (Z3) — the embedded `TriageOutput`.
// AH6 (rivoli-ai/conductor#713): SchemaVersion bumped to 2 with the
// addition of optional `IssueDisplayId` (ISSUE-99 short form). The
// andy-tasks consumer reads this and pins it on the resulting Goal so
// the reciprocal Story↔Goal linkage round-trips end-to-end. Optional
// for back-compat: pre-AH6 producer emissions left it null and the
// consumer falls back to the GUID-form `IssueId`.
public sealed record IssueEventPayload(
    Guid IssueId,
    Guid? RepositoryId,
    string OwnerUserId,
    string Title,
    string TriageState,
    string? TriagedBy,
    DateTimeOffset? TriagedAt,
    TriageOutput? TriageOutput = null,
    string? IssueDisplayId = null)
{
    public const int SchemaVersion = 2;

    public int Schema_Version => SchemaVersion;
}

public enum IssueEventKind
{
    Triaged,
    Accepted,
    Rejected,
    // Z5 — human edits to the triage output (PATCH /output, POST
    // /revert). NOT emitted when the agent produces a new revision —
    // that path goes through CompleteTriage which fires `triaged`.
    Revised
}

public static class IssueEventKindExtensions
{
    public static string ToSubjectKind(this IssueEventKind kind) => kind switch
    {
        IssueEventKind.Triaged => "triaged",
        IssueEventKind.Accepted => "accepted",
        IssueEventKind.Rejected => "rejected",
        IssueEventKind.Revised => "revised",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
