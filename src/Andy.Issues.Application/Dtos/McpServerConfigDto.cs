// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

// Outbound shape: no environment values, no headers, no stdio env vars.
// The sandbox-injection path reads the full entity server-side and never
// goes through this DTO.
public record McpServerConfigDto(
    Guid Id,
    string? OwnerUserId,
    bool IsShared,
    string Name,
    string? Description,
    string Type,
    bool Enabled,
    string? Command,
    string? ArgumentsJson,
    string? Url,
    bool HasEnvironment,
    bool HasHeaders,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
