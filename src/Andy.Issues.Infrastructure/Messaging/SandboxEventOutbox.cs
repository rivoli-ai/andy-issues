// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;

namespace Andy.Issues.Infrastructure.Messaging;

// Helper for appending a sandbox.* OutboxEntry to the DbContext in the
// same unit of work as the Sandbox change. Caller controls
// SaveChangesAsync — the outbox row lands with whatever else is
// pending, so dual-write consistency is preserved by EF's transaction
// scope.
public static class SandboxEventOutbox
{
    public static void AppendSandboxEvent(
        this AppDbContext db,
        Sandbox sandbox,
        SandboxEventKind kind,
        string? reason = null)
    {
        var payload = new SandboxEventPayload(
            SandboxId: sandbox.Id,
            ContainerId: sandbox.ContainerId,
            RepositoryId: sandbox.RepositoryId,
            Branch: sandbox.Branch,
            Status: sandbox.Status.ToString(),
            Reason: reason);

        db.Outbox.Add(new OutboxEntry
        {
            Id = Guid.NewGuid(),
            Subject = $"andy.issues.events.sandbox.{sandbox.Id}.{kind.ToSubjectKind()}",
            PayloadType = typeof(SandboxEventPayload).FullName,
            PayloadJson = JsonSerializer.Serialize(payload, EventJson.Options),
            CorrelationId = sandbox.Id,
            CausationId = null,
            Generation = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
