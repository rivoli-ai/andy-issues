// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;

namespace Andy.Issues.Infrastructure.Messaging;

// Helper for appending a story.* OutboxEntry to the DbContext in the
// same unit of work as the UserStory change. Caller controls
// SaveChangesAsync — the outbox row lands with whatever else is
// pending, so dual-write consistency is preserved by EF's transaction
// scope.
//
// The caller is responsible for supplying the fully-populated Feature
// and Epic graph (or the loose ids) so the payload can carry them.
public static class StoryEventOutbox
{
    // Build and attach a story event outbox row. Does not SaveChanges.
    //
    // `featureId`, `epicId`, `repositoryId` are passed explicitly so
    // callers can hand them over without a second DB round trip when
    // they already navigated the Feature.Epic chain.
    public static void AppendStoryEvent(
        this AppDbContext db,
        UserStory story,
        Guid featureId,
        Guid epicId,
        Guid repositoryId,
        StoryEventKind kind)
    {
        var payload = new StoryEventPayload(
            StoryId: story.Id,
            FeatureId: featureId,
            EpicId: epicId,
            RepositoryId: repositoryId,
            Title: story.Title,
            Status: story.Status.ToString(),
            DisplayId: story.DisplayId);

        var subject = $"andy.issues.events.story.{story.Id}.{kind.ToSubjectKind()}";

        db.Outbox.Add(new OutboxEntry
        {
            Id = Guid.NewGuid(),
            Subject = subject,
            PayloadType = typeof(StoryEventPayload).FullName,
            PayloadJson = JsonSerializer.Serialize(payload, EventJson.Options),
            CorrelationId = story.Id,
            CausationId = null,
            Generation = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
