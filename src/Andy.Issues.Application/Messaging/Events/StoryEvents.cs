// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Messaging.Events;

// Payload for andy.issues.events.story.{storyId}.{kind} events per
// ADR 0001 §2. Serialised with EventJson.Options (snake_case) when
// written to the outbox.
public sealed record StoryEventPayload(
    Guid StoryId,
    Guid FeatureId,
    Guid EpicId,
    Guid RepositoryId,
    string Title,
    string Status,
    // AH1 — short human-readable identifier (`STORY-{seq}`) derived
    // from the entity's `Seq`. Older consumers that ignored unknown
    // fields keep working; consumers that care can cross-reference
    // without a GUID round-trip. Nullable so payloads replayed from
    // pre-AH1 outbox rows decode cleanly.
    string? DisplayId = null)
{
    public const int SchemaVersion = 1;

    public int Schema_Version => SchemaVersion;
}

// Four kinds mirror the BacklogService hook points:
//   Created  — AddStoryAsync
//   Readied  — UpdateStoryStatusAsync → Ready
//   Done     — UpdateStoryStatusAsync → Done
//   Updated  — UpdateStoryAsync, or UpdateStoryStatusAsync with any
//              target status other than Ready/Done
public enum StoryEventKind
{
    Created,
    Readied,
    Done,
    Updated
}

public static class StoryEventKindExtensions
{
    public static string ToSubjectKind(this StoryEventKind kind) => kind switch
    {
        StoryEventKind.Created => "created",
        StoryEventKind.Readied => "readied",
        StoryEventKind.Done => "done",
        StoryEventKind.Updated => "updated",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
