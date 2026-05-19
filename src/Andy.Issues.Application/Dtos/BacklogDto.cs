// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

public record BacklogDto(
    Guid RepositoryId,
    IReadOnlyList<EpicDto> Epics);

public record EpicDto(
    Guid Id,
    string DisplayId,
    Guid RepositoryId,
    string Title,
    string? Description,
    int Order,
    string? ExternalId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<FeatureDto> Features);

public record FeatureDto(
    Guid Id,
    string DisplayId,
    Guid EpicId,
    string Title,
    string? Description,
    int Order,
    string? ExternalId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<UserStoryDto> Stories);

public record UserStoryDto(
    Guid Id,
    string DisplayId,
    Guid FeatureId,
    string Title,
    string? Description,
    string? AcceptanceCriteria,
    int? StoryPoints,
    string Status,
    string? PullRequestUrl,
    int Order,
    string? ExternalId,
    int? AzureDevOpsWorkItemId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    // SP.7.1 — stable sha256 over title/body/labels/AC. Emitted on
    // every DTO so downstream consumers (andy-tasks, Conductor) can
    // detect drift after a re-import. See andy-issues#181 /
    // conductor#1627. Nullable for back-compat with older clients that
    // ignore unknown fields; the field is always populated for stories
    // persisted post-migration.
    string? ContentHash = null);
