// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Enums;

// SP.0.4 (andy-issues#180 / conductor#1632) — priority classification
// emitted by the story triage agent. Lower ordinal = more urgent. Wire
// values are lowercased ("p0", "p1", ...) by the controller JSON enum
// converter (System.Text.Json default + JsonStringEnumConverter); the
// outbox uses snake-case via EventJson.Options and lands on the same
// strings since the names contain no underscores.
public enum StoryPriority
{
    P0 = 0,
    P1 = 1,
    P2 = 2,
    P3 = 3
}
