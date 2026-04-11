// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

public record BacklogDto(
    Guid RepositoryId,
    IReadOnlyList<EpicDto> Epics);

public record EpicDto(
    Guid Id,
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
    Guid FeatureId,
    string Title,
    string? Description,
    string? AcceptanceCriteria,
    int? StoryPoints,
    string Status,
    string? PullRequestUrl,
    int Order,
    string? ExternalId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
