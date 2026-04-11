// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;

namespace Andy.Issues.Application.Interfaces;

public enum RepositoryScope
{
    Mine = 0,
    Shared = 1,
    All = 2
}

public enum ShareResult
{
    Created = 0,
    AlreadyShared = 1,
    SelfShareRejected = 2,
    EmailNotFound = 3,
    NotFound = 4,
    NotOwner = 5
}

public interface IRepositoryService
{
    Task<PagedResult<RepositoryDto>> ListAsync(
        string userId,
        RepositoryScope scope,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<RepositoryDto?> GetAsync(Guid id, string userId, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, string userId, CancellationToken ct = default);

    Task<(ShareResult Result, RepositoryShareDto? Dto)> ShareAsync(
        Guid repositoryId,
        string email,
        string ownerUserId,
        CancellationToken ct = default);

    Task<bool> UnshareAsync(
        Guid repositoryId,
        string targetUserId,
        string ownerUserId,
        CancellationToken ct = default);

    Task<IReadOnlyList<RepositoryShareDto>?> ListSharesAsync(
        Guid repositoryId,
        string ownerUserId,
        CancellationToken ct = default);
}
