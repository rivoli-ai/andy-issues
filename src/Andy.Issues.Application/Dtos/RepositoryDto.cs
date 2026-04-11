// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

public record RepositoryDto(
    Guid Id,
    string OwnerUserId,
    string Name,
    string? Description,
    string Provider,
    string CloneUrl,
    string DefaultBranch,
    string? ExternalId,
    Guid? LlmSettingId,
    bool HasAzureIdentity,
    string CodeIndexStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public record RepositoryShareDto(
    Guid Id,
    Guid RepositoryId,
    string SharedWithUserId,
    string GrantedByUserId,
    DateTimeOffset GrantedAt);
