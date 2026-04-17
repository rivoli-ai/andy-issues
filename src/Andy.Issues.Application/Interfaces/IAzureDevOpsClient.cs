// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Interfaces;

public record AzureDevOpsRepositoryInfo(
    string ExternalId,
    string Name,
    string? Description,
    string CloneUrl,
    string DefaultBranch,
    string Project,
    string Organization);

public record AzureDevOpsWorkItemUpsert(
    int? ExistingId,
    string Title,
    string? Description,
    string State);

public record AzureDevOpsWorkItemSnapshot(
    int Id,
    string Title,
    string State);

public record AzureDevOpsPullRequestInfo(int Id, string Url);

public record AzureDevOpsFeedInfo(string Id, string Name, string? Description, string? Url);

public record AzureDevOpsUserInfo(string DisplayName);

/// <summary>
/// Result of an org-scoped PAT verification — returned by
/// <see cref="IAzureDevOpsClient.VerifyConnectionAsync"/>. The id / display
/// name fields come straight from the ConnectionData payload's
/// <c>authenticatedUser</c> object.
/// </summary>
public record AzureDevOpsConnectionInfo(string AuthenticatedUserId, string DisplayName);

public interface IAzureDevOpsClient
{
    /// <summary>
    /// Validates an Azure DevOps PAT by calling the connection data API.
    /// Returns user info on success or null if the token is invalid.
    /// </summary>
    Task<AzureDevOpsUserInfo?> GetCurrentUserAsync(
        string personalAccessToken,
        CancellationToken ct = default);

    /// <summary>
    /// Validates a PAT against a specific organization by calling
    /// <c>https://dev.azure.com/{organization}/_apis/ConnectionData</c>.
    /// Returns null if the PAT is invalid or the org is unreachable.
    /// </summary>
    Task<AzureDevOpsConnectionInfo?> VerifyConnectionAsync(
        string organization,
        string personalAccessToken,
        CancellationToken ct = default);

    Task<AzureDevOpsRepositoryInfo?> GetRepositoryAsync(
        string organization,
        string project,
        string repositoryId,
        string personalAccessToken,
        CancellationToken ct = default);

    Task<AzureDevOpsWorkItemSnapshot?> UpsertWorkItemAsync(
        string organization,
        string project,
        AzureDevOpsWorkItemUpsert item,
        string personalAccessToken,
        CancellationToken ct = default);

    Task<IReadOnlyList<AzureDevOpsWorkItemSnapshot>> GetWorkItemsAsync(
        string organization,
        string project,
        IReadOnlyList<int> ids,
        string personalAccessToken,
        CancellationToken ct = default);

    Task<AzureDevOpsPullRequestInfo?> CreatePullRequestAsync(
        string organization,
        string project,
        string repositoryId,
        string title,
        string? description,
        string sourceBranch,
        string targetBranch,
        string personalAccessToken,
        CancellationToken ct = default);

    Task<IReadOnlyList<AzureDevOpsFeedInfo>> ListFeedsAsync(
        string organization,
        string personalAccessToken,
        CancellationToken ct = default);
}
