// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Messaging.Events;

// Incoming payload from andy-containers' run.* publisher (see
// andy-containers ADR 0001 and the contract in andy-containers'
// RunEventPayload). This service is the consumer, not the publisher,
// so we declare the shape here to deserialize into.
//
// A run correlates to **either** a UserStory (sandbox-created PR work)
// **or** an Issue (Z2 — headless triage agent run), never both. Both
// fields are nullable; when neither is set the consumer acks and skips
// (e.g. runs created directly via the web UI).
public sealed record ContainerRunEventPayload(
    Guid RunId,
    Guid? StoryId,
    string Status,
    int? ExitCode,
    double? DurationSeconds,
    Guid? IssueId = null);
