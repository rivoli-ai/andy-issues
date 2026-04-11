// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Domain.Entities;

namespace Andy.Issues.Application.Mapping;

public static class BacklogMapping
{
    public static UserStoryDto ToDto(this UserStory entity) => new(
        entity.Id,
        entity.FeatureId,
        entity.Title,
        entity.Description,
        entity.AcceptanceCriteria,
        entity.StoryPoints,
        entity.Status.ToString(),
        entity.PullRequestUrl,
        entity.Order,
        entity.ExternalId,
        entity.CreatedAt,
        entity.UpdatedAt);

    public static FeatureDto ToDto(this Feature entity) => new(
        entity.Id,
        entity.EpicId,
        entity.Title,
        entity.Description,
        entity.Order,
        entity.ExternalId,
        entity.CreatedAt,
        entity.UpdatedAt,
        entity.Stories
            .OrderBy(s => s.Order)
            .Select(ToDto)
            .ToList());

    public static EpicDto ToDto(this Epic entity) => new(
        entity.Id,
        entity.RepositoryId,
        entity.Title,
        entity.Description,
        entity.Order,
        entity.ExternalId,
        entity.CreatedAt,
        entity.UpdatedAt,
        entity.Features
            .OrderBy(f => f.Order)
            .Select(ToDto)
            .ToList());

    public static BacklogDto ToBacklogDto(this Repository repository) => new(
        repository.Id,
        repository.Epics
            .OrderBy(e => e.Order)
            .Select(ToDto)
            .ToList());
}
