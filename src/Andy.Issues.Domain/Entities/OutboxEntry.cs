// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Entities;

// Transactional outbox row per ADR 0001. Publishers write to this table
// in the same transaction as the domain change that produced the message;
// the OutboxDispatcher background worker drains pending rows to the bus.
// This guarantees at-least-once delivery without dual-write consistency
// problems — a crash between commit and publish leaves the row pending
// and it is picked up on the next dispatcher tick.
public class OutboxEntry
{
    // Primary key. Also used as the MsgId when published — the outbox
    // row id IS the message id, so reprocessing is naturally idempotent
    // from the consumer's perspective.
    public Guid Id { get; set; }

    // NATS subject per the taxonomy in ADR 0001:
    //   andy.<service>.events.<entity>.<id>.<kind>
    public string Subject { get; set; } = string.Empty;

    // Fully-qualified CLR type name of the payload. Optional — subjects
    // already encode intent, but this is a useful safety net for
    // consumers picking a deserialization target.
    public string? PayloadType { get; set; }

    // JSON-encoded payload. Stored as text for provider portability.
    public string PayloadJson { get; set; } = "{}";

    // Correlation chain, copied into MessageHeaders at publish time.
    public Guid CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public int Generation { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Nullable sentinels for dispatch state. PublishedAt is set on the
    // first successful publish; rows are never deleted so the outbox
    // doubles as an audit log.
    public DateTimeOffset? PublishedAt { get; set; }

    // Attempt counter and last-error diagnostics for failed publishes.
    // The dispatcher uses these to implement exponential backoff and to
    // surface poison messages.
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}
