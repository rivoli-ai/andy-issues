// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Domain.Entities;

namespace Andy.Issues.Application.Mapping;

public static class LinkedProviderMapping
{
    public static LinkedProviderDto ToDto(this LinkedProvider entity) => new(
        entity.Id,
        entity.Provider.ToString(),
        entity.AccountLogin,
        entity.ExpiresAt,
        entity.CreatedAt,
        entity.UpdatedAt);
}
