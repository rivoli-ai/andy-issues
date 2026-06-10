// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.PullRequests;

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
        _issueResponses.Clear();
        ListIssuesException = null;
        _subIssueResponses.Clear();
        _addLabelsCalls.Clear();
        _createIssueCalls.Clear();
        _addSubIssueCalls.Clear();
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

    /// <summary>
    /// When non-null, <see cref="ListIssuesAsync"/> throws this
    /// exception instead of returning a list — simulates 401/403/404
    /// responses from the real API.
    /// </summary>
    public Exception? ListIssuesException { get; set; }

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
        if (ListIssuesException is not null)
            throw ListIssuesException;
        _issueResponses.TryGetValue($"{owner}/{repo}", out var issues);
        return Task.FromResult(issues ?? (IReadOnlyList<GitHubIssueInfo>)Array.Empty<GitHubIssueInfo>());
    }

    private readonly ConcurrentDictionary<string, IReadOnlyList<int>> _subIssueResponses = new();

    public void SetSubIssues(string owner, string repo, int issueNumber, IReadOnlyList<int> subIssueNumbers)
    {
        _subIssueResponses[$"{owner}/{repo}#{issueNumber}"] = subIssueNumbers;
    }

    public Task<IReadOnlyList<int>> ListSubIssueNumbersAsync(
        string owner,
        string repo,
        int issueNumber,
        string accessToken,
        CancellationToken ct = default)
    {
        _subIssueResponses.TryGetValue($"{owner}/{repo}#{issueNumber}", out var numbers);
        return Task.FromResult(numbers ?? (IReadOnlyList<int>)Array.Empty<int>());
    }

    private readonly List<GitHubRepositoryInfo> _userRepositories = new();

    public void SetUserRepositories(IEnumerable<GitHubRepositoryInfo> repos)
    {
        _userRepositories.Clear();
        _userRepositories.AddRange(repos);
    }

    public Task<IReadOnlyList<GitHubRepositoryInfo>> ListUserRepositoriesAsync(
        string accessToken,
        string? search,
        int page,
        int perPage,
        CancellationToken ct = default)
    {
        var filtered = string.IsNullOrWhiteSpace(search)
            ? _userRepositories.AsEnumerable()
            : _userRepositories.Where(r => r.FullName.Contains(search, StringComparison.OrdinalIgnoreCase));
        var pageSlice = filtered
            .Skip(Math.Max(0, page - 1) * perPage)
            .Take(perPage)
            .ToList();
        return Task.FromResult<IReadOnlyList<GitHubRepositoryInfo>>(pageSlice);
    }

    // ── Write methods (recategorize write-back) ──────────────────────

    private readonly ConcurrentBag<(string owner, string repo, int issueNumber, IReadOnlyList<string> labels)> _addLabelsCalls = new();
    public IReadOnlyCollection<(string owner, string repo, int issueNumber, IReadOnlyList<string> labels)> AddLabelsCalls => _addLabelsCalls;

    public Task AddLabelsAsync(
        string owner,
        string repo,
        int issueNumber,
        IReadOnlyList<string> labels,
        string accessToken,
        CancellationToken ct = default)
    {
        _addLabelsCalls.Add((owner, repo, issueNumber, labels));
        return Task.CompletedTask;
    }

    private readonly ConcurrentBag<(string owner, string repo, string title, string? body, IReadOnlyList<string> labels)> _createIssueCalls = new();
    public IReadOnlyCollection<(string owner, string repo, string title, string? body, IReadOnlyList<string> labels)> CreateIssueCalls => _createIssueCalls;

    private int _nextCreatedIssueNumber = 100;

    public Task<GitHubCreatedIssue> CreateIssueAsync(
        string owner,
        string repo,
        string title,
        string? body,
        IReadOnlyList<string> labels,
        string accessToken,
        CancellationToken ct = default)
    {
        _createIssueCalls.Add((owner, repo, title, body, labels));
        var number = Interlocked.Increment(ref _nextCreatedIssueNumber);
        return Task.FromResult(new GitHubCreatedIssue(number, number * 1000L));
    }

    private readonly ConcurrentBag<(string owner, string repo, int parentIssueNumber, long childIssueId)> _addSubIssueCalls = new();
    public IReadOnlyCollection<(string owner, string repo, int parentIssueNumber, long childIssueId)> AddSubIssueCalls => _addSubIssueCalls;

    public Task AddSubIssueAsync(
        string owner,
        string repo,
        int parentIssueNumber,
        long childIssueId,
        string accessToken,
        CancellationToken ct = default)
    {
        _addSubIssueCalls.Add((owner, repo, parentIssueNumber, childIssueId));
        return Task.CompletedTask;
    }

    private readonly ConcurrentDictionary<string, PullRequestStatusInfo> _prStatuses = new();

    public void SetPullRequestStatus(string owner, string repo, int number, PullRequestStatusInfo status)
    {
        _prStatuses[$"{owner}/{repo}/{number}"] = status;
    }

    public Task<PullRequestStatusInfo?> GetPullRequestStatusAsync(
        string owner,
        string repo,
        int number,
        string accessToken,
        CancellationToken ct = default)
    {
        _prStatuses.TryGetValue($"{owner}/{repo}/{number}", out var status);
        return Task.FromResult<PullRequestStatusInfo?>(status);
    }
}
