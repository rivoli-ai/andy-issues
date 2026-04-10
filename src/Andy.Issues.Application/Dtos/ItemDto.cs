// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

public record ItemDto(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
