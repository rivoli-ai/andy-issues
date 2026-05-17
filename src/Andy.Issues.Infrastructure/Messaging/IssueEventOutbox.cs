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
//
// Z4 — `causationId`/`parentGeneration` parameters carry the message-id
// chain when the transition was driven by an upstream event. The Z2
// `ContainerRunEventConsumer` will pass the run-finished message's
// `msg-id` as `causationId` and its `generation` as `parentGeneration`.
// User-driven transitions (REST, CLI, MCP) leave both at the default;
// the resulting outbox row is the root of its causation chain.
public static class IssueEventOutbox
{
    public static void AppendIssueEvent(
        this AppDbContext db,
        Issue issue,
        IssueEventKind kind,
        Guid? causationId = null,
        int parentGeneration = 0)
    {
        var payload = new IssueEventPayload(
            IssueId: issue.Id,
            RepositoryId: issue.RepositoryId,
            OwnerUserId: issue.OwnerUserId,
            Title: issue.Title,
            TriageState: issue.TriageState.ToString(),
            TriagedBy: issue.TriagedBy,
            TriagedAt: issue.TriagedAt,
            TriageOutput: issue.TriageOutput,
            // AH6 (rivoli-ai/conductor#713): carry the short ISSUE-N
            // identifier so the andy-tasks consumer can pin it on the
            // resulting Goal and emit it back on
            // GoalCreatedEvent.SourceIssueDisplayId. Falls through as
            // null for issues with Seq == 0 (legacy fixtures or
            // pre-AH6 rows that haven't been backfilled yet) — the
            // andy-tasks side already tolerates that case.
            IssueDisplayId: issue.Seq > 0 ? issue.DisplayId : null);

        var subject = $"andy.issues.events.issue.{issue.Id}.{kind.ToSubjectKind()}";

        // Per ADR-0001 §5: an event published in response to a parent
        // message is `parent.generation + 1`; a root event (no parent)
        // is generation 0.
        var generation = causationId is null ? 0 : parentGeneration + 1;

        db.Outbox.Add(new OutboxEntry
        {
            Id = Guid.NewGuid(),
            Subject = subject,
            PayloadType = typeof(IssueEventPayload).FullName,
            PayloadJson = JsonSerializer.Serialize(payload, EventJson.Options),
            CorrelationId = issue.Id,
            CausationId = causationId,
            Generation = generation,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
