// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.ValueTypes;

namespace Andy.Issues.Domain.Entities;

// Z5 — versioned audit row for a triage output. One row per agent
// completion or human edit. The Issue.TriageOutput field is the
// materialised "current" view; this table is the history.
//
// Storage shape mirrors Issue.TriageOutput's JSON column — same
// EventJson.Options serialisation so the wire schema is identical.
//
// AuthorKind drives the revised-event emission rule: Human revisions
// fire `andy.issues.events.issue.<id>.revised`; Agent revisions
// arrive via CompleteTriage which fires `triaged` (no double-emit).
public class TriageOutputRevision
{
    public Guid Id { get; set; }
    public Guid IssueId { get; set; }

    // Who produced this revision. For Agent revisions, the field
    // matches the agent's run id or slug (e.g. "triage-agent");
    // populated from `triagedBy` on CompleteTriage. For Human
    // revisions, it is the user id from the authenticated principal.
    public string Author { get; set; } = string.Empty;
    public TriageRevisionAuthorKind AuthorKind { get; set; }

    public TriageOutput TriageOutput { get; set; } = null!;

    // Optional diff summary — short human-readable description of
    // what changed relative to the prior revision. Conductor's AB5
    // version timeline renders this; null is acceptable.
    public string? DiffSummary { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
