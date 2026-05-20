// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Services;

namespace Andy.Issues.Application.Mapping;

public static class BacklogMapping
{
    public static UserStoryDto ToDto(this UserStory entity) => entity.ToDto(triaging: false);

    public static UserStoryDto ToDto(this UserStory entity, bool triaging) => new(
        entity.Id,
        entity.DisplayId,
        entity.FeatureId,
        entity.Title,
        entity.Description,
        entity.AcceptanceCriteria,
        entity.StoryPoints,
        entity.Status.ToString(),
        entity.PullRequestUrl,
        entity.Order,
        entity.ExternalId,
        entity.AzureDevOpsWorkItemId,
        entity.CreatedAt,
        entity.UpdatedAt,
        // SP.7.1 — emit the hash on every DTO so consumers can detect
        // drift. We compute on demand rather than read the column so
        // rows that pre-date the migration (ContentHash IS NULL) still
        // ship the correct hash — the persisted column is a cache, the
        // canonical truth is the entity content.
        entity.ContentHash ?? StoryContentHasher.Compute(entity),
        // SP.0.4 — derived triage state + refinement output. `triaging`
        // is set by the orchestrator while a run is in flight; pure
        // GETs see one of NotTriaged / Triaged / Obsolete.
        entity.DeriveTriageState(triaging),
        entity.ToRefinementDto());

    public static FeatureDto ToDto(this Feature entity) => new(
        entity.Id,
        entity.DisplayId,
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
        entity.DisplayId,
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
