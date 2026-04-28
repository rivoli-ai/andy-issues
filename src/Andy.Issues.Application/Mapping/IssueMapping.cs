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
        entity.CreatedAt,
        entity.UpdatedAt);
}
