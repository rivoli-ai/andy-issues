// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Domain.Entities;

// #103 — durable record of a draft-backlog generation run. The row
// is created when the user starts a generation, then advanced
// through phases (the tracker writes UpdatedAt on every transition).
// CompletedAt is set when the run reaches a terminal phase.
//
// Rows survive past the run so the UI's late-join fallback (GET
// /api/generations/{id}) can resync state if the SignalR hub
// connection drops mid-run, and so generation history can be
// surfaced as audit later.
public class BacklogGeneration
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public string UserId { get; set; } = string.Empty;

    public BacklogGenerationPhase Phase { get; set; } = BacklogGenerationPhase.Pending;

    // Free-form context — short message describing the current
    // phase's substep, e.g. "Calling gpt-4o" or "Saving 12 epics".
    // Null is acceptable.
    public string? Detail { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
