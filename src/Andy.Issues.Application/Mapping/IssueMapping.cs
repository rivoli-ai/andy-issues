// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Domain.Entities;

namespace Andy.Issues.Application.Mapping;

public static class IssueMapping
{
    public static IssueDto ToDto(this Issue entity) => new(
        entity.Id,
        entity.OwnerUserId,
        entity.RepositoryId,
        entity.Title,
        entity.Body,
        entity.TriageState.ToString(),
        entity.TriagedAt,
        entity.TriagedBy,
        entity.TriageOutput,
        entity.CreatedAt,
        entity.UpdatedAt);

    // #187 — lightweight projection for the unified `GET /api/issues`
    // listing endpoint. Skips `Body` and `TriageOutput` so a page of
    // 50 rows stays small enough for the cockpit pipeline kanban and
    // intake pane to render incrementally.
    public static IssueSummary ToSummary(this Issue entity) => new(
        entity.Id,
        entity.DisplayId,
        entity.OwnerUserId,
        entity.AssigneeUserId,
        entity.RepositoryId,
        entity.Title,
        entity.TriageState.ToString(),
        entity.TriagedAt,
        entity.TriagedBy,
        entity.CreatedAt,
        entity.UpdatedAt);
}
