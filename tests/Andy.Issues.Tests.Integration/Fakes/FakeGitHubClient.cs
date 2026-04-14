// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Integration.Fakes;

public class FakeGitHubClient : IGitHubClient
{
    private readonly ConcurrentDictionary<string, GitHubRepositoryInfo> _responses = new();
    private readonly ConcurrentBag<(string owner, string repo, string title, string? description, string head, string baseBranch)> _prCalls = new();

    public GitHubPullRequestInfo? PullRequestResult { get; set; }
    public IReadOnlyCollection<(string owner, string repo, string title, string? description, string head, string baseBranch)> PullRequestCalls => _prCalls;

    public void AddResponse(string fullName, GitHubRepositoryInfo info)
    {
        _responses[fullName] = info;
    }

    public GitHubUserInfo? CurrentUserResult { get; set; } = new("fake-gh-user");

    public void Reset()
    {
        _responses.Clear();
        _prCalls.Clear();
        PullRequestResult = null;
        CurrentUserResult = new("fake-gh-user");
    }

    public Task<GitHubUserInfo?> GetCurrentUserAsync(
        string accessToken, CancellationToken ct = default) =>
        Task.FromResult(CurrentUserResult);

    public Task<GitHubRepositoryInfo?> GetRepositoryAsync(
        string fullName,
        string accessToken,
        CancellationToken ct = default)
    {
        _responses.TryGetValue(fullName, out var info);
        return Task.FromResult<GitHubRepositoryInfo?>(info);
    }

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
        _prCalls.Add((owner, repo, title, description, head, baseBranch));
        return Task.FromResult(PullRequestResult);
    }

    private readonly ConcurrentDictionary<string, IReadOnlyList<GitHubIssueInfo>> _issueResponses = new();

    public void SetIssues(string owner, string repo, IReadOnlyList<GitHubIssueInfo> issues)
    {
        _issueResponses[$"{owner}/{repo}"] = issues;
    }

    public Task<IReadOnlyList<GitHubIssueInfo>> ListIssuesAsync(
        string owner,
        string repo,
        string accessToken,
        CancellationToken ct = default)
    {
        _issueResponses.TryGetValue($"{owner}/{repo}", out var issues);
        return Task.FromResult(issues ?? (IReadOnlyList<GitHubIssueInfo>)Array.Empty<GitHubIssueInfo>());
    }
}
