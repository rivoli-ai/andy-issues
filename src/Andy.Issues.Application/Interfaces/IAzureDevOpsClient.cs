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

public interface IAzureDevOpsClient
{
    Task<AzureDevOpsRepositoryInfo?> GetRepositoryAsync(
        string organization,
        string project,
        string repositoryId,
        string personalAccessToken,
        CancellationToken ct = default);
}
