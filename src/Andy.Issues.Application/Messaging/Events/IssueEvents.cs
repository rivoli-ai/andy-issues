// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.ValueTypes;

namespace Andy.Issues.Application.Messaging.Events;

// Payload for andy.issues.events.issue.{issueId}.{kind} events per
// ADR 0001 §2 (Z1) + Z3.
//
// Z3 — `TriageOutput` carries the full agent-produced classification
// when the issue reaches `Triaged`/`Accepted`/`Rejected`. Null on
// payloads emitted before an output was attached (e.g. a manual
// CompleteTriage during Z1 testing). Schema stays at v1 — the
// existing fields are unchanged; the new field is additive and
// optional, so consumers built against the Z1 envelope deserialize
// cleanly.
public sealed record IssueEventPayload(
    Guid IssueId,
    Guid? RepositoryId,
    string OwnerUserId,
    string Title,
    string TriageState,
    string? TriagedBy,
    DateTimeOffset? TriagedAt,
    TriageOutput? TriageOutput = null)
{
    public const int SchemaVersion = 1;

    public int Schema_Version => SchemaVersion;
}

public enum IssueEventKind
{
    Triaged,
    Accepted,
    Rejected
}

public static class IssueEventKindExtensions
{
    public static string ToSubjectKind(this IssueEventKind kind) => kind switch
    {
        IssueEventKind.Triaged => "triaged",
        IssueEventKind.Accepted => "accepted",
        IssueEventKind.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
