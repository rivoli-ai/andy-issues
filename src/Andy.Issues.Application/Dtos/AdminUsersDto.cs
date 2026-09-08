// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

public record AdminUserDto(string UserId, string? Email, string? DisplayName, IReadOnlyList<string> Roles, DateTimeOffset? LastSeenAt);
public record AdminUsersDto(IReadOnlyList<AdminUserDto> Items, int Total);
