// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Domain.Entities;

namespace Andy.Issues.Application.Mapping;

public static class SandboxMapping
{
    public static SandboxDto ToDto(this Sandbox entity) => new(
        entity.Id,
        entity.ContainerId,
        entity.RepositoryId,
        entity.OwnerUserId,
        entity.Branch,
        entity.Status.ToString(),
        entity.IdeEndpoint,
        entity.VncEndpoint,
        entity.CreatedAt,
        entity.UpdatedAt);
}
