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

public record GitHubPullRequestInfo(int Number, string Url);

public record GitHubUserInfo(string Login);

public interface IGitHubClient
{
    /// <summary>
    /// Validates a GitHub PAT by calling GET /user. Returns the user info
    /// on success or null if the token is invalid/expired.
    /// </summary>
    Task<GitHubUserInfo?> GetCurrentUserAsync(
        string accessToken,
        CancellationToken ct = default);

    Task<GitHubRepositoryInfo?> GetRepositoryAsync(
        string fullName,
        string accessToken,
        CancellationToken ct = default);

    Task<GitHubPullRequestInfo?> CreatePullRequestAsync(
        string owner,
        string repo,
        string title,
        string? description,
        string head,
        string baseBranch,
        string accessToken,
        CancellationToken ct = default);
}
