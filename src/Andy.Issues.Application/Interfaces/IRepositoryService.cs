// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;

namespace Andy.Issues.Application.Interfaces;

public enum RepositoryScope
{
    Mine = 0,
    Shared = 1,
    All = 2
}

public enum CreateRepositoryResult
{
    Created = 0,
    InvalidProvider = 1,
    InvalidCloneUrl = 2,
    AlreadyExists = 3
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
    /// <summary>
    /// Creates a new repository owned by the supplied user. Used by the
    /// Conductor "Add repository" flow which knows the clone URL but
    /// has not gone through the GitHub/Azure DevOps OAuth dance — the
    /// `sync-github` / `sync-azure` endpoints both require a linked
    /// access token, so they cannot be used to seed the very first
    /// repo. Returns `Conflict` when the same owner already has a
    /// repository at the same clone URL so the call is naturally
    /// idempotent.
    /// </summary>
    Task<(CreateRepositoryResult Result, RepositoryDto? Dto)> CreateAsync(
        CreateRepositoryRequest request,
        string ownerUserId,
        CancellationToken ct = default);

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

    Task<SyncResult?> SyncFromGitHubAsync(
        string userId,
        IReadOnlyList<string> fullNames,
        CancellationToken ct = default);

    Task<SyncResult?> SyncFromAzureDevOpsAsync(
        string userId,
        string organization,
        string? project,
        IReadOnlyList<string> repositoryIds,
        CancellationToken ct = default);

    Task<SetLlmResult> SetLlmSettingAsync(
        Guid repositoryId,
        Guid? llmSettingId,
        string ownerUserId,
        CancellationToken ct = default);

    Task<SetAzureIdentityResult> SetAzureIdentityAsync(
        Guid repositoryId,
        string clientId,
        string clientSecret,
        string tenantId,
        string? subscriptionId,
        string ownerUserId,
        CancellationToken ct = default);

    Task<VerifyAzureIdentityResult?> VerifyAzureIdentityAsync(
        Guid repositoryId,
        string ownerUserId,
        CancellationToken ct = default);
}

public enum SetLlmResult
{
    Updated = 0,
    RepositoryNotFound = 1,
    LlmSettingNotFound = 2,
    NotOwner = 3
}

public enum SetAzureIdentityResult
{
    Updated = 0,
    NotFound = 1,
    NotOwner = 2
}

public record VerifyAzureIdentityResult(bool Success, string Message);
