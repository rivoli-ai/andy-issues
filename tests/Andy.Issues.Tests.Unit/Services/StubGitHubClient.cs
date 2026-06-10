// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.PullRequests;

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

    public List<(string owner, string repo)> ListIssuesCalls { get; } = new();
    public List<string?> ListIssuesTokens { get; } = new();
    private readonly Dictionary<string, IReadOnlyList<GitHubIssueInfo>> _issueResponses = new();

    /// <summary>
    /// When non-null, ListIssuesAsync throws this exception instead of
    /// returning a list — simulates GitHub API failures (401/403/404).
    /// </summary>
    public Exception? ListIssuesException { get; set; }

    public StubGitHubClient IssuesFor(string owner, string repo, IReadOnlyList<GitHubIssueInfo> issues)
    {
        _issueResponses[$"{owner}/{repo}"] = issues;
        return this;
    }

    // #99 — list user repositories. Tests can preload the result via
    // `UserRepositories` and the stub will paginate that list with
    // optional substring search.
    public List<GitHubRepositoryInfo> UserRepositories { get; } = new();
    public string? LastUserReposSearch { get; private set; }
    public int LastUserReposPage { get; private set; }
    public int LastUserReposPerPage { get; private set; }

    public Task<IReadOnlyList<GitHubRepositoryInfo>> ListUserRepositoriesAsync(
        string accessToken,
        string? search,
        int page,
        int perPage,
        CancellationToken ct = default)
    {
        LastUserReposSearch = search;
        LastUserReposPage = page;
        LastUserReposPerPage = perPage;
        var filtered = string.IsNullOrWhiteSpace(search)
            ? UserRepositories.AsEnumerable()
            : UserRepositories.Where(r => r.FullName.Contains(search, StringComparison.OrdinalIgnoreCase));
        var pageSlice = filtered
            .Skip(Math.Max(0, page - 1) * perPage)
            .Take(perPage)
            .ToList();
        return Task.FromResult<IReadOnlyList<GitHubRepositoryInfo>>(pageSlice);
    }

    public Task<IReadOnlyList<GitHubIssueInfo>> ListIssuesAsync(
        string owner,
        string repo,
        string accessToken,
        CancellationToken ct = default)
    {
        ListIssuesCalls.Add((owner, repo));
        ListIssuesTokens.Add(accessToken);
        if (ListIssuesException is not null)
            throw ListIssuesException;
        _issueResponses.TryGetValue($"{owner}/{repo}", out var issues);
        return Task.FromResult(issues ?? (IReadOnlyList<GitHubIssueInfo>)Array.Empty<GitHubIssueInfo>());
    }

    // Native sub-issues: keyed by "owner/repo#number". The importer
    // only calls this for classified epics/features whose
    // SubIssuesTotal > 0.
    public List<(string owner, string repo, int issueNumber)> ListSubIssuesCalls { get; } = new();
    private readonly Dictionary<string, IReadOnlyList<int>> _subIssueResponses = new();

    public StubGitHubClient SubIssuesFor(
        string owner, string repo, int issueNumber, IReadOnlyList<int> subIssueNumbers)
    {
        _subIssueResponses[$"{owner}/{repo}#{issueNumber}"] = subIssueNumbers;
        return this;
    }

    public Task<IReadOnlyList<int>> ListSubIssueNumbersAsync(
        string owner,
        string repo,
        int issueNumber,
        string accessToken,
        CancellationToken ct = default)
    {
        ListSubIssuesCalls.Add((owner, repo, issueNumber));
        _subIssueResponses.TryGetValue($"{owner}/{repo}#{issueNumber}", out var numbers);
        return Task.FromResult(numbers ?? (IReadOnlyList<int>)Array.Empty<int>());
    }

    public Dictionary<string, PullRequestStatusInfo?> PrStatuses { get; } = new();

    public Task<PullRequestStatusInfo?> GetPullRequestStatusAsync(
        string owner,
        string repo,
        int number,
        string accessToken,
        CancellationToken ct = default)
    {
        PrStatuses.TryGetValue($"{owner}/{repo}/{number}", out var status);
        return Task.FromResult(status);
    }
}
