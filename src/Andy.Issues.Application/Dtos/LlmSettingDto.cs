// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

// Outbound shape: never exposes the API key.
public record LlmSettingDto(
    Guid Id,
    string OwnerUserId,
    string Name,
    string Provider,
    string Model,
    string? BaseUrl,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
