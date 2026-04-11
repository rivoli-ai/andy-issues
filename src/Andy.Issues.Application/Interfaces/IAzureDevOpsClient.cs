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

public interface IAzureDevOpsClient
{
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
}
