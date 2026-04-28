// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Messaging.Events;

// Payload for andy.issues.events.issue.{issueId}.{kind} events per
// ADR 0001 §2 (Z1). The triaged event's full schema (including triage
// output) is finalized in Z3/Z4; this record is the Z1 envelope and
// will be extended once those land.
public sealed record IssueEventPayload(
    Guid IssueId,
    Guid? RepositoryId,
    string OwnerUserId,
    string Title,
    string TriageState,
    string? TriagedBy,
    DateTimeOffset? TriagedAt)
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
