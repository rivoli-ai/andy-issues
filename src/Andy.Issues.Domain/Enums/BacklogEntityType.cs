// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Enums;

/// <summary>
/// The three top-level backlog entity types that carry a short
/// display identifier allocated from an independent per-type
/// sequence (<c>EPIC-42</c> / <c>FEAT-7</c> / <c>STORY-13</c>).
/// See AH1.
/// </summary>
public enum BacklogEntityType
{
    Epic = 0,
    Feature = 1,
    Story = 2
}
