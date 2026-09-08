// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;

namespace Andy.Issues.Application.Interfaces;

public interface IAndyRbacUsersClient
{
    Task<AdminUsersDto> ListAsync(string userId, string? query, string? role, int skip, int take, CancellationToken ct = default);
}
