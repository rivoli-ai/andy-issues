// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Domain.Entities;

namespace Andy.Issues.Application.Mapping;

public static class LlmSettingMapping
{
    public static LlmSettingDto ToDto(this LlmSetting entity) => new(
        entity.Id,
        entity.OwnerUserId,
        entity.Name,
        entity.Provider.ToString(),
        entity.Model,
        entity.BaseUrl,
        entity.IsDefault,
        entity.CreatedAt,
        entity.UpdatedAt);
}
