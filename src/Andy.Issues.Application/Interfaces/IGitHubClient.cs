// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Interfaces;

public record GitHubRepositoryInfo(
    string ExternalId,
    string Name,
    string FullName,
    string? Description,
    string CloneUrl,
    string DefaultBranch);

public interface IGitHubClient
{
    Task<GitHubRepositoryInfo?> GetRepositoryAsync(
        string fullName,
        string accessToken,
        CancellationToken ct = default);
}
