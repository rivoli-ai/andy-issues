// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;

namespace Andy.Issues.Application.Messaging.Events;

// SP.0.4 (andy-issues#180 / conductor#1632) — emitted on the andy-issues
// outbound NATS topic when a story refinement run completes. Subject is
//   `andy.issues.events.story.{storyId}.triaged`
// so the existing `story.*` wildcard subscription on the Conductor event
// bus picks it up alongside `created` / `readied` / `done` / `updated`.
//
// Payload carries the full refinement output and the derived triage
// state so the chat panel can transition the local state machine and
// re-render the RefinementPanel without a refetch. Schema-versioned for
// forward-compat — bumps to v2 will ship as a separate sibling record
// (mirrors TriageOutput's frozen-at-v1 convention).
//
// Serialised through `EventJson.Options` (snake_case) so wire-level
// keys are `story_id`, `refine_version`, `content_hash_at_triage`, etc.
public sealed record StoryTriageCompletedEvent(
    Guid StoryId,
    Guid FeatureId,
    Guid EpicId,
    Guid RepositoryId,
    string? DisplayId,
    int RefineVersion,
    DateTimeOffset RefinedAt,
    string RefinedBy,
    string? ContentHashAtTriage,
    StoryClassificationDto Classification,
    StoryTriageStateDto TriageState)
{
    public const int SchemaVersion = 1;

    public int Schema_Version => SchemaVersion;
}
