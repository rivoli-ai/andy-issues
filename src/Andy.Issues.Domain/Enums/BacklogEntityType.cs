// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Enums;

/// <summary>
/// Entity types in andy-issues that carry a short display
/// identifier allocated from an independent per-type sequence:
/// <c>EPIC-42</c> / <c>FEAT-7</c> / <c>STORY-13</c> / <c>ISSUE-99</c>.
/// </summary>
/// <remarks>
/// AH1 introduced the first three (the backlog hierarchy). AH6
/// (rivoli-ai/conductor#713) adds Issue so the triage envelope
/// carries an <c>ISSUE-N</c> identifier on the wire — andy-tasks
/// then pins it on the resulting Goal and emits it back via
/// <c>GoalCreatedEvent.SourceIssueDisplayId</c>, closing the
/// reciprocal Story↔Goal linkage loop.
///
/// Numeric values are persisted as the primary key of
/// <c>backlog_sequences</c> — never renumber.
/// </remarks>
public enum BacklogEntityType
{
    Epic = 0,
    Feature = 1,
    Story = 2,
    // AH6 (rivoli-ai/conductor#713): triage envelope. Display form
    // is `ISSUE-{n}`. Sequence is independent of Epic/Feature/Story.
    Issue = 3,
}
