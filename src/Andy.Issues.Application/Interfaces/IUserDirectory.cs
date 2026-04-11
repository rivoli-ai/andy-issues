// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Interfaces;

public record UserRecord(string UserId, string Email, string? DisplayName);

public interface IUserDirectory
{
    Task<UserRecord?> FindByEmailAsync(string email, CancellationToken ct = default);

    Task<UserRecord?> FindByUserIdAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<UserRecord>> SuggestAsync(
        string query,
        string excludeUserId,
        int limit,
        CancellationToken ct = default);

    Task UpsertAsync(UserRecord record, CancellationToken ct = default);
}
