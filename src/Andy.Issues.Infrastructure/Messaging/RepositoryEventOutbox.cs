// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;

namespace Andy.Issues.Infrastructure.Messaging;

// Helpers for appending repository.* OutboxEntry rows to the DbContext
// in the same unit of work as the Repository change. Two helpers
// because the two event kinds carry different payload shapes.
public static class RepositoryEventOutbox
{
    public static void AppendRepositoryRegistered(
        this AppDbContext db,
        Repository repo)
    {
        var payload = new RepositoryRegisteredPayload(
            RepositoryId: repo.Id,
            Provider: repo.Provider.ToString(),
            Name: repo.Name,
            CloneUrl: repo.CloneUrl);

        db.Outbox.Add(new OutboxEntry
        {
            Id = Guid.NewGuid(),
            Subject = $"andy.issues.events.repository.{repo.Id}.{RepositoryEventKind.Registered.ToSubjectKind()}",
            PayloadType = typeof(RepositoryRegisteredPayload).FullName,
            PayloadJson = JsonSerializer.Serialize(payload, EventJson.Options),
            CorrelationId = repo.Id,
            CausationId = null,
            Generation = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    public static void AppendRepositorySynced(
        this AppDbContext db,
        Repository repo,
        int added,
        int updated,
        int skipped,
        int errorCount)
    {
        var payload = new RepositorySyncedPayload(
            RepositoryId: repo.Id,
            Provider: repo.Provider.ToString(),
            Added: added,
            Updated: updated,
            Skipped: skipped,
            ErrorCount: errorCount);

        db.Outbox.Add(new OutboxEntry
        {
            Id = Guid.NewGuid(),
            Subject = $"andy.issues.events.repository.{repo.Id}.{RepositoryEventKind.Synced.ToSubjectKind()}",
            PayloadType = typeof(RepositorySyncedPayload).FullName,
            PayloadJson = JsonSerializer.Serialize(payload, EventJson.Options),
            CorrelationId = repo.Id,
            CausationId = null,
            Generation = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
