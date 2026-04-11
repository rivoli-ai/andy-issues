// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Domain.Entities;

namespace Andy.Issues.Application.Mapping;

public static class McpServerConfigMapping
{
    public static McpServerConfigDto ToDto(this McpServerConfig entity) => new(
        entity.Id,
        entity.OwnerUserId,
        entity.IsShared,
        entity.Name,
        entity.Description,
        entity.Type.ToString(),
        entity.Enabled,
        entity.Command,
        entity.ArgumentsJson,
        entity.Url,
        HasEnvironment: !string.IsNullOrEmpty(entity.EnvironmentJson),
        HasHeaders: !string.IsNullOrEmpty(entity.HeadersJson),
        entity.CreatedAt,
        entity.UpdatedAt);
}
