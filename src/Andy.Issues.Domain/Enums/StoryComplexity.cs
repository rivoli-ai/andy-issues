// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Enums;

// SP.0.4 (andy-issues#180 / conductor#1632) — t-shirt complexity sizing
// emitted by the story triage agent. Ordinals carry no quantitative
// meaning; consumers (Conductor's RefinementPanel, the Risk-vs-Cost
// matrix) map them to display labels and rough effort buckets.
public enum StoryComplexity
{
    Trivial = 0,
    Small = 1,
    Medium = 2,
    Large = 3,
    Xl = 4
}
