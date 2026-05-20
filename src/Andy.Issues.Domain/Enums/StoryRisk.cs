// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Enums;

// SP.0.4 (andy-issues#180 / conductor#1632) — risk classification
// emitted by the story triage agent. Three buckets is the minimum
// Conductor's RefinementPanel needs; finer-grained gradations (e.g.
// "Operational" vs "Security") can land later as a sibling enum.
public enum StoryRisk
{
    Low = 0,
    Medium = 1,
    High = 2
}
