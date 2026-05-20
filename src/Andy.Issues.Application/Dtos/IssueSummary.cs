// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

// #187 — lightweight projection of <see cref="IssueDto"/> for the
// unified `GET /api/issues` listing endpoint. Drops the full
// <c>TriageOutput</c> payload (which can be several KB per row) so the
// cockpit pipeline kanban (AF2) and intake pane (AF3) can fetch a page
// of headers without paying for the agent-produced classification
// blob on every row. Callers hit the existing `GET /api/triage/{id}`
// to hydrate a single issue's full body when they need it.
public record IssueSummary(
    Guid Id,
    string DisplayId,
    string OwnerUserId,
    string? AssigneeUserId,
    Guid? RepositoryId,
    string Title,
    string TriageState,
    DateTimeOffset? TriagedAt,
    string? TriagedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
