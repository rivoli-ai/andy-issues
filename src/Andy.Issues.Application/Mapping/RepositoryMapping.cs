// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Domain.Entities;

namespace Andy.Issues.Application.Mapping;

public static class RepositoryMapping
{
    public static RepositoryDto ToDto(this Repository entity) => new(
        entity.Id,
        entity.OwnerUserId,
        entity.Name,
        entity.Description,
        entity.Provider.ToString(),
        entity.CloneUrl,
        entity.DefaultBranch,
        entity.ExternalId,
        entity.LlmSettingId,
        entity.HasAzureIdentity,
        entity.CodeIndexStatus.ToString(),
        entity.CreatedAt,
        entity.UpdatedAt);

    public static RepositoryShareDto ToDto(this RepositoryShare entity) => new(
        entity.Id,
        entity.RepositoryId,
        entity.SharedWithUserId,
        entity.GrantedByUserId,
        entity.GrantedAt);
}
