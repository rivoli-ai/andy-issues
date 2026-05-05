// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.PullRequests;

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

/// <summary>
/// A GitHub issue as returned by <c>GET /repos/{owner}/{repo}/issues</c>.
/// The <see cref="IsPullRequest"/> flag is surfaced so callers can skip
/// PRs — that endpoint returns both issues and PRs in the same list.
/// </summary>
/// <summary>
/// A GitHub issue as returned by <c>GET /repos/{owner}/{repo}/issues</c>.
/// <para>
/// <see cref="Type"/> is GitHub's typed Issue Type
/// (<c>"Bug"</c> / <c>"Feature"</c> / <c>"Task"</c> — the new typed
/// Issue Types feature). <c>null</c> when the source issue has no
/// type set. Stored alongside labels so classification changes on
/// re-sync can be detected without needing to remember the old
/// label set. See conductor#670 Bug 2.
/// </para>
/// </summary>
public record GitHubIssueInfo(
    int Number,
    string Title,
    string? Body,
    string State,
    bool IsPullRequest,
    IReadOnlyList<string> Labels,
    string? Type = null);

/// <summary>
/// Thrown when a GitHub API call fails in a way the caller needs to
/// distinguish from "the repo has no issues". Carries the HTTP status
/// code so callers (e.g. the backlog importer) can surface
/// actionable messages for 401/403/404 without re-parsing strings.
/// </summary>
public class GitHubApiException : Exception
{
    public int? StatusCode { get; }

    public GitHubApiException(string message, int? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }
}

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

    /// <summary>
    /// Lists repositories accessible to the authenticated user via
    /// <c>GET /user/repos</c>. The optional <paramref name="search"/>
    /// is a substring match against full_name on the server side
    /// (GitHub's own search syntax — `q=user:&lt;login&gt; foo`),
    /// scoped to repos the user can see. Pagination is offset/per-page;
    /// callers loop until they get a short page.
    /// </summary>
    Task<IReadOnlyList<GitHubRepositoryInfo>> ListUserRepositoriesAsync(
        string accessToken,
        string? search,
        int page,
        int perPage,
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

    /// <summary>
    /// Lists all issues in a repository (both open and closed). The
    /// underlying GitHub endpoint returns pull requests alongside
    /// issues — each item's <see cref="GitHubIssueInfo.IsPullRequest"/>
    /// flag lets callers filter them out.
    /// </summary>
    Task<IReadOnlyList<GitHubIssueInfo>> ListIssuesAsync(
        string owner,
        string repo,
        string accessToken,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches a pull request's lifecycle state via
    /// <c>GET /repos/{owner}/{repo}/pulls/{number}</c>. Returns null if
    /// the PR is not visible (404). State is normalised to
    /// "open" | "closed" | "merged" — GitHub returns "open" / "closed"
    /// with a separate <c>merged</c> flag, which this method collapses
    /// into the merged state when both are true.
    /// </summary>
    Task<PullRequestStatusInfo?> GetPullRequestStatusAsync(
        string owner,
        string repo,
        int number,
        string accessToken,
        CancellationToken ct = default);
}
