// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.ValueTypes;

namespace Andy.Issues.Application.Dtos;

// Wire shape for Issue (REST/MCP/CLI). `TriageOutput` is null until
// CompleteTriage runs with an output payload (Z3); the field is the
// same Domain record so consumers see the canonical schema, not a
// rebuilt projection.
public record IssueDto(
    Guid Id,
    string OwnerUserId,
    Guid? RepositoryId,
    string Title,
    string? Body,
    string TriageState,
    DateTimeOffset? TriagedAt,
    string? TriagedBy,
    TriageOutput? TriageOutput,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
