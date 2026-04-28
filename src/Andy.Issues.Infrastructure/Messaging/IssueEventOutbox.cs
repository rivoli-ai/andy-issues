// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;

namespace Andy.Issues.Infrastructure.Messaging;

// Helper for appending an issue.* OutboxEntry to the DbContext in the
// same unit of work as the Issue change. Mirrors StoryEventOutbox: the
// caller controls SaveChangesAsync, so the outbox row lands in the
// same transaction as the state mutation (ADR 0001 §3).
public static class IssueEventOutbox
{
    public static void AppendIssueEvent(
        this AppDbContext db,
        Issue issue,
        IssueEventKind kind)
    {
        var payload = new IssueEventPayload(
            IssueId: issue.Id,
            RepositoryId: issue.RepositoryId,
            OwnerUserId: issue.OwnerUserId,
            Title: issue.Title,
            TriageState: issue.TriageState.ToString(),
            TriagedBy: issue.TriagedBy,
            TriagedAt: issue.TriagedAt,
            TriageOutput: issue.TriageOutput);

        var subject = $"andy.issues.events.issue.{issue.Id}.{kind.ToSubjectKind()}";

        db.Outbox.Add(new OutboxEntry
        {
            Id = Guid.NewGuid(),
            Subject = subject,
            PayloadType = typeof(IssueEventPayload).FullName,
            PayloadJson = JsonSerializer.Serialize(payload, EventJson.Options),
            CorrelationId = issue.Id,
            CausationId = null,
            Generation = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
