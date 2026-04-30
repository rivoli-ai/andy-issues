// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

// #103 — wire shape for a draft-backlog generation run. Surfaced via
// GET /api/generations/{id} (late-join resync) and pushed over
// SignalR on every phase advance for live progress UI.
public record BacklogGenerationDto(
    Guid Id,
    Guid RepositoryId,
    string UserId,
    string Phase,
    string? Detail,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);
