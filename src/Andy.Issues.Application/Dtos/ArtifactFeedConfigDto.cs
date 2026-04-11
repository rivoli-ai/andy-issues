// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

public record ArtifactFeedConfigDto(
    Guid Id,
    string Name,
    string Organization,
    string FeedName,
    string? Project,
    string Type,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
