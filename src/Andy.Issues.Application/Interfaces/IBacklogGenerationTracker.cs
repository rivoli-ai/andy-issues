// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Application.Interfaces;

// #103 — single entry point for advancing a draft-backlog
// generation through its phases. Persists the phase transition on
// the BacklogGeneration row AND fires a SignalR event to the repo
// group so connected clients drive a live progress UI.
//
// Callers are the DraftBacklogGenerator (Infrastructure) and any
// future hosted-service refactor; this interface keeps the
// concerns testable in isolation from the generator's prompt/parse
// logic.
public interface IBacklogGenerationTracker
{
    // Creates a new BacklogGeneration row in Pending and returns its
    // id. Surfaces a clean DTO for the caller.
    Task<BacklogGenerationDto> StartAsync(
        Guid repositoryId,
        string userId,
        CancellationToken ct = default);

    // Transitions an existing generation to a new phase. `detail` is
    // a short human-readable substep description. When `phase` is a
    // terminal phase (Completed/Failed/Cancelled) the row's
    // CompletedAt is stamped.
    Task<BacklogGenerationDto?> AdvanceAsync(
        Guid generationId,
        BacklogGenerationPhase phase,
        string? detail = null,
        CancellationToken ct = default);

    // Owner-scoped point lookup. Used by GET /api/generations/{id}
    // for the late-join resync path.
    Task<BacklogGenerationDto?> GetAsync(
        Guid generationId,
        string userId,
        CancellationToken ct = default);
}
