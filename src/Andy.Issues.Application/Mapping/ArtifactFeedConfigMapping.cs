// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Domain.Entities;

namespace Andy.Issues.Application.Mapping;

public static class ArtifactFeedConfigMapping
{
    public static ArtifactFeedConfigDto ToDto(this ArtifactFeedConfig entity) => new(
        entity.Id,
        entity.Name,
        entity.Organization,
        entity.FeedName,
        entity.Project,
        entity.Type.ToString(),
        entity.Enabled,
        entity.CreatedAt,
        entity.UpdatedAt);
}
