// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

// #187 — cursor-paginated response for `GET /api/issues`. Distinct
// from <see cref="PagedResult{T}"/> (which exposes a numeric page +
// total) because the cockpit consumers (AF2 pipeline kanban, AF3
// intake pane) need stable forward pagination over a frequently-
// mutating set. The cursor is an opaque, base64-encoded composite
// of `(CreatedAt, Id)` from the last row of the current page; null
// when there are no more rows.
public record IssueListResponse(
    IReadOnlyList<IssueSummary> Items,
    string? Cursor);
