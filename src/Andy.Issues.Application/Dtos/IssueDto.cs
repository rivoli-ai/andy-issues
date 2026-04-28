// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

public record IssueDto(
    Guid Id,
    string OwnerUserId,
    Guid? RepositoryId,
    string Title,
    string? Body,
    string TriageState,
    DateTimeOffset? TriagedAt,
    string? TriagedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
