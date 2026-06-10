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
/// <param name="SubIssuesTotal">
/// Count of GitHub native sub-issues, read from
/// <c>sub_issues_summary.total</c> in the issues-list payload. Zero
/// when the issue has no sub-issues or the field is absent/null.
/// </param>
/// <param name="SubIssueNumbers">
/// Issue numbers of GitHub native sub-issues. NOT populated by the
/// list call — the importer fills this (via
/// <see cref="IGitHubClient.ListSubIssueNumbersAsync"/>) only for
/// classified epics/features whose <paramref name="SubIssuesTotal"/>
/// is greater than zero.
/// </param>
/// <param name="Id">
/// GitHub's global database id for the issue (the numeric <c>id</c>
/// field, distinct from the per-repo <c>number</c>). Required by the
/// sub-issues write API (<c>POST .../sub_issues</c> takes
/// <c>sub_issue_id</c>, not a number). Zero when not parsed.
/// </param>
public record GitHubIssueInfo(
    int Number,
    string Title,
    string? Body,
    string State,
    bool IsPullRequest,
    IReadOnlyList<string> Labels,
    string? Type = null,
    int SubIssuesTotal = 0,
    IReadOnlyList<int>? SubIssueNumbers = null,
    long Id = 0);

/// <summary>
/// Result of <see cref="IGitHubClient.CreateIssueAsync"/>. Carries
/// both identifiers: <paramref name="Number"/> for ExternalId
/// bookkeeping (<c>gh:{number}</c>) and the database
/// <paramref name="Id"/> for sub-issue linking.
/// </summary>
public record GitHubCreatedIssue(int Number, long Id);

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
    /// Lists the issue numbers of a parent issue's GitHub native
    /// sub-issues via
    /// <c>GET /repos/{owner}/{repo}/issues/{n}/sub_issues</c>.
    /// Returns an empty list when the issue has none or the fetch
    /// fails — a sub-issues failure must never fail the whole sync;
    /// hierarchy inference falls back to task-list parsing.
    /// </summary>
    Task<IReadOnlyList<int>> ListSubIssueNumbersAsync(
        string owner,
        string repo,
        int issueNumber,
        string accessToken,
        CancellationToken ct = default);

    /// <summary>
    /// Adds labels to an existing issue via
    /// <c>POST /repos/{owner}/{repo}/issues/{issueNumber}/labels</c>.
    /// This is a write — a token is required. Throws
    /// <see cref="GitHubApiException"/> on a non-success response so
    /// callers can record a per-item error and continue.
    /// </summary>
    Task AddLabelsAsync(
        string owner,
        string repo,
        int issueNumber,
        IReadOnlyList<string> labels,
        string accessToken,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a new issue via
    /// <c>POST /repos/{owner}/{repo}/issues</c>. This is a write — a
    /// token is required. Throws <see cref="GitHubApiException"/> on a
    /// non-success response.
    /// </summary>
    Task<GitHubCreatedIssue> CreateIssueAsync(
        string owner,
        string repo,
        string title,
        string? body,
        IReadOnlyList<string> labels,
        string accessToken,
        CancellationToken ct = default);

    /// <summary>
    /// Links a child issue under a parent via GitHub's native
    /// sub-issues write API,
    /// <c>POST /repos/{owner}/{repo}/issues/{parentIssueNumber}/sub_issues</c>.
    /// The child is referenced by its database
    /// <see cref="GitHubIssueInfo.Id"/> (NOT its number). This is a
    /// write — a token is required. Throws
    /// <see cref="GitHubApiException"/> on a non-success response
    /// (including 422 when the child already has a parent).
    /// </summary>
    Task AddSubIssueAsync(
        string owner,
        string repo,
        int parentIssueNumber,
        long childIssueId,
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
