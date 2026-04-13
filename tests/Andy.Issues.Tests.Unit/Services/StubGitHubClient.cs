// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Unit.Services;

public class StubGitHubClient : IGitHubClient
{
    private readonly Dictionary<string, GitHubRepositoryInfo?> _responses = new();

    public StubGitHubClient Returns(string fullName, GitHubRepositoryInfo? info)
    {
        _responses[fullName] = info;
        return this;
    }

    public GitHubUserInfo? CurrentUserResult { get; set; } = new("stub-user");

    public Task<GitHubUserInfo?> GetCurrentUserAsync(
        string accessToken, CancellationToken ct = default) =>
        Task.FromResult(CurrentUserResult);

    public Task<GitHubRepositoryInfo?> GetRepositoryAsync(
        string fullName,
        string accessToken,
        CancellationToken ct = default)
    {
        _responses.TryGetValue(fullName, out var info);
        return Task.FromResult(info);
    }

    public List<(string owner, string repo, string title, string? description, string head, string baseBranch)> PullRequestCalls { get; } = new();
    public GitHubPullRequestInfo? PullRequestResult { get; set; }

    public Task<GitHubPullRequestInfo?> CreatePullRequestAsync(
        string owner,
        string repo,
        string title,
        string? description,
        string head,
        string baseBranch,
        string accessToken,
        CancellationToken ct = default)
    {
        PullRequestCalls.Add((owner, repo, title, description, head, baseBranch));
        return Task.FromResult(PullRequestResult);
    }
}
