// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

// SP.0.4 — body returned with HTTP 202 from POST /api/stories/{id}/refine.
// The client polls the story (GET /api/stories/{id}) or subscribes to
// the andy-issues outbound NATS topic
// (`andy.issues.events.story.{storyId}.triaged`) to observe completion.
//
// `RefineVersion` is the value that the in-flight run targets; clients
// can use {storyId, refineVersion} as the local idempotency key so a
// reconnect-driven retry within the 5-minute window correlates against
// the same logical run.
public sealed record StoryRefineRunDto(
    Guid RefineRunId,
    int RefineVersion);
