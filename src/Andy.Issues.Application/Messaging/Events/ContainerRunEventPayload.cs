// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Messaging.Events;

// Incoming payload from andy-containers' run.* publisher (see
// andy-containers ADR 0001 and the contract in andy-containers'
// RunEventPayload). This service is the consumer, not the publisher,
// so we declare the shape here to deserialize into.
//
// `StoryId` is nullable because andy-containers may emit events for
// runs that were not created via andy-issues (e.g. from the web UI
// directly). In that case we ack and skip — there is no backlog row
// to correlate.
public sealed record ContainerRunEventPayload(
    Guid RunId,
    Guid? StoryId,
    string Status,
    int? ExitCode,
    double? DurationSeconds);
