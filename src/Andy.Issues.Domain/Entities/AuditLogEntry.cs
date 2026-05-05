// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Entities;

// Append-only record of sensitive actions (agent-rules edits, future
// AI-config reads). Distinct from the OutboxEntry stream — outbox is
// for async event delivery to NATS, AuditLogEntry is the immutable
// who/what/when trail kept for compliance and incident response.
public class AuditLogEntry
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
