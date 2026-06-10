// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.PullRequests;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.External;

public class GitHubClient : IGitHubClient
{
    private const string BaseUrl = "https://api.github.com";
    private const string UserAgent = "andy-issues";
    private const string ApiVersion = "2022-11-28";

    private readonly HttpClient _http;
    private readonly ILogger<GitHubClient> _logger;

    public GitHubClient(HttpClient http, ILogger<GitHubClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<GitHubUserInfo?> GetCurrentUserAsync(
        string accessToken,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("GitHub GET /user failed with {Status}", response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var login = doc.RootElement.GetProperty("login").GetString() ?? "";
        return new GitHubUserInfo(login);
    }

    public async Task<GitHubRepositoryInfo?> GetRepositoryAsync(
        string fullName,
        string accessToken,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/repos/{fullName}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("GitHub GET /repos/{FullName} failed with {Status}",
                fullName, response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        return new GitHubRepositoryInfo(
            ExternalId: root.GetProperty("id").GetRawText(),
            Name: root.GetProperty("name").GetString() ?? fullName,
            FullName: root.GetProperty("full_name").GetString() ?? fullName,
            Description: root.TryGetProperty("description", out var desc) && desc.ValueKind != JsonValueKind.Null
                ? desc.GetString() : null,
            CloneUrl: root.GetProperty("clone_url").GetString() ?? "",
            DefaultBranch: root.GetProperty("default_branch").GetString() ?? "main");
    }

    // #99 — list repositories the authenticated user can see. GitHub
    // exposes this at GET /user/repos with offset/per-page paging.
    // The optional `search` is forwarded as a substring match — GitHub
    // doesn't filter /user/repos by name natively, so we filter
    // server-side after fetching the page (small enough to be cheap).
    public async Task<IReadOnlyList<GitHubRepositoryInfo>> ListUserRepositoriesAsync(
        string accessToken,
        string? search,
        int page,
        int perPage,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (perPage < 1) perPage = 30;
        if (perPage > 100) perPage = 100;

        var url = $"{BaseUrl}/user/repos?per_page={perPage}&page={page}&sort=full_name";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("GitHub GET /user/repos failed with {Status}", response.StatusCode);
            return Array.Empty<GitHubRepositoryInfo>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var items = new List<GitHubRepositoryInfo>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var fullName = element.GetProperty("full_name").GetString() ?? "";
            if (!string.IsNullOrWhiteSpace(search)
                && !fullName.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;

            items.Add(new GitHubRepositoryInfo(
                ExternalId: element.GetProperty("id").GetRawText(),
                Name: element.GetProperty("name").GetString() ?? "",
                FullName: fullName,
                Description: element.TryGetProperty("description", out var desc)
                    && desc.ValueKind != JsonValueKind.Null ? desc.GetString() : null,
                CloneUrl: element.GetProperty("clone_url").GetString() ?? "",
                DefaultBranch: element.GetProperty("default_branch").GetString() ?? "main"));
        }
        return items;
    }

    public async Task<IReadOnlyList<GitHubIssueInfo>> ListIssuesAsync(
        string owner,
        string repo,
        string accessToken,
        CancellationToken ct = default)
    {
        var results = new List<GitHubIssueInfo>();
        // GitHub's issues endpoint paginates via the Link header. Start
        // with an explicit query string and walk `rel="next"` links
        // until none remain. `state=all` pulls both open and closed;
        // per_page=100 is the documented maximum.
        var nextUrl = $"{BaseUrl}/repos/{owner}/{repo}/issues?state=all&per_page=100";

        while (!string.IsNullOrEmpty(nextUrl))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            // Allow unauthenticated calls for public repos. GitHub
            // accepts anonymous requests at 60/hr/IP and refuses
            // private-repo reads with a 404 (indistinguishable from a
            // non-existent repo) or 401/403 when partial visibility
            // is involved.
            if (!string.IsNullOrEmpty(accessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // Distinguish the three user-facing failure modes so
                // the caller can surface an actionable message. We
                // leak the exception rather than silently returning
                // an empty list because "no issues" and "couldn't
                // talk to GitHub" must not collapse into the same
                // outcome for the caller's UX.
                var status = (int)response.StatusCode;
                var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var v)
                    ? v.FirstOrDefault() : null;
                string message = status switch
                {
                    401 => "GitHub rejected the token (unauthorized).",
                    403 when remaining == "0" =>
                        string.IsNullOrEmpty(accessToken)
                            ? "GitHub rate limit reached (60/hr unauthenticated). Link a PAT for 5000/hr."
                            : "GitHub rate limit reached for the linked PAT.",
                    403 => "GitHub forbade the request — check the PAT's scopes.",
                    404 =>
                        string.IsNullOrEmpty(accessToken)
                            ? $"Repository '{owner}/{repo}' is not publicly accessible — link a GitHub PAT."
                            : $"Repository '{owner}/{repo}' not found — or the PAT can't see it.",
                    _ => $"GitHub request failed with HTTP {status}."
                };
                _logger.LogWarning(
                    "GitHub GET /repos/{Owner}/{Repo}/issues failed with {Status}",
                    owner, repo, response.StatusCode);
                throw new GitHubApiException(message, status);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var number = item.TryGetProperty("number", out var n) ? n.GetInt32() : 0;
                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var body = item.TryGetProperty("body", out var b) && b.ValueKind != JsonValueKind.Null
                    ? b.GetString() : null;
                var state = item.TryGetProperty("state", out var s) ? s.GetString() ?? "open" : "open";
                var isPr = item.TryGetProperty("pull_request", out var pr) && pr.ValueKind != JsonValueKind.Null;

                var labels = new List<string>();
                if (item.TryGetProperty("labels", out var labelsEl) && labelsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var label in labelsEl.EnumerateArray())
                    {
                        // Labels can be plain strings (older API) or
                        // objects with `name`. Handle both.
                        if (label.ValueKind == JsonValueKind.String)
                        {
                            var name = label.GetString();
                            if (!string.IsNullOrEmpty(name)) labels.Add(name);
                        }
                        else if (label.ValueKind == JsonValueKind.Object
                                 && label.TryGetProperty("name", out var nameEl))
                        {
                            var name = nameEl.GetString();
                            if (!string.IsNullOrEmpty(name)) labels.Add(name);
                        }
                    }
                }

                // GitHub's typed Issue Types feature. When the repo
                // has types enabled and the issue has one assigned, the
                // API returns `type: { name: "Bug", ... }`. Absent or
                // null when the repo doesn't use types or the issue
                // is unassigned. See conductor#670 Bug 2.
                string? issueType = null;
                if (item.TryGetProperty("type", out var typeEl)
                    && typeEl.ValueKind == JsonValueKind.Object
                    && typeEl.TryGetProperty("name", out var typeNameEl)
                    && typeNameEl.ValueKind == JsonValueKind.String)
                {
                    var typeName = typeNameEl.GetString();
                    if (!string.IsNullOrWhiteSpace(typeName)) issueType = typeName;
                }

                // GitHub native sub-issues: the list payload carries a
                // `sub_issues_summary` object (often null) whose
                // `total` tells us whether a follow-up call to the
                // sub_issues endpoint is worthwhile. See
                // ListSubIssueNumbersAsync.
                int subIssuesTotal = 0;
                if (item.TryGetProperty("sub_issues_summary", out var subSummaryEl)
                    && subSummaryEl.ValueKind == JsonValueKind.Object
                    && subSummaryEl.TryGetProperty("total", out var subTotalEl)
                    && subTotalEl.ValueKind == JsonValueKind.Number)
                {
                    subIssuesTotal = subTotalEl.GetInt32();
                }

                results.Add(new GitHubIssueInfo(
                    number, title, body, state, isPr, labels, issueType, subIssuesTotal));
            }

            nextUrl = ParseNextLink(response.Headers);
        }

        return results;
    }

    /// <summary>
    /// Lists the issue numbers of a parent issue's GitHub native
    /// sub-issues via
    /// <c>GET /repos/{owner}/{repo}/issues/{issueNumber}/sub_issues</c>.
    /// Anonymous calls are allowed (public repos); pagination walks
    /// the Link header like <see cref="ListIssuesAsync"/>. On a
    /// non-success status this logs a warning and returns an EMPTY
    /// list — a sub-issues fetch failure must not fail the whole
    /// sync; hierarchy inference simply falls back to task-list
    /// parsing for that issue.
    /// </summary>
    public async Task<IReadOnlyList<int>> ListSubIssueNumbersAsync(
        string owner,
        string repo,
        int issueNumber,
        string accessToken,
        CancellationToken ct = default)
    {
        var results = new List<int>();
        var nextUrl = $"{BaseUrl}/repos/{owner}/{repo}/issues/{issueNumber}/sub_issues?per_page=100";

        while (!string.IsNullOrEmpty(nextUrl))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            if (!string.IsNullOrEmpty(accessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GitHub GET /repos/{Owner}/{Repo}/issues/{Number}/sub_issues failed with {Status} — falling back to task-list parsing",
                    owner, repo, issueNumber, response.StatusCode);
                return Array.Empty<int>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("number", out var n)
                    && n.ValueKind == JsonValueKind.Number)
                {
                    results.Add(n.GetInt32());
                }
            }

            nextUrl = ParseNextLink(response.Headers);
        }

        return results;
    }

    /// <summary>
    /// Parses the <c>rel="next"</c> entry from GitHub's Link header.
    /// Returns null when absent, signalling end of pagination.
    /// </summary>
    internal static string? ParseNextLink(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Link", out var linkValues))
            return null;

        foreach (var header in linkValues)
        {
            // Link: <https://api.github.com/...?page=2>; rel="next", <...>; rel="last"
            foreach (var part in header.Split(','))
            {
                var trimmed = part.Trim();
                var relIdx = trimmed.IndexOf("rel=\"next\"", StringComparison.Ordinal);
                if (relIdx < 0) continue;
                var urlStart = trimmed.IndexOf('<');
                var urlEnd = trimmed.IndexOf('>');
                if (urlStart >= 0 && urlEnd > urlStart)
                    return trimmed.Substring(urlStart + 1, urlEnd - urlStart - 1);
            }
        }
        return null;
    }

    public async Task<GitHubPullRequestInfo?> CreatePullRequestAsync(
        string owner,
        string repo,
        string title,
        string? description,
        string head,
        string baseBranch,
        string accessToken,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/repos/{owner}/{repo}/pulls");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Content = JsonContent.Create(new
        {
            title,
            body = description,
            head,
            @base = baseBranch
        });

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("GitHub POST /repos/{Owner}/{Repo}/pulls failed with {Status}: {Body}",
                owner, repo, response.StatusCode, body);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        var number = root.TryGetProperty("number", out var n) ? n.GetInt32() : 0;
        var url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
        return new GitHubPullRequestInfo(number, url);
    }

    public async Task<PullRequestStatusInfo?> GetPullRequestStatusAsync(
        string owner,
        string repo,
        int number,
        string accessToken,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{BaseUrl}/repos/{owner}/{repo}/pulls/{number}");
        if (!string.IsNullOrEmpty(accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("GitHub GET /repos/{Owner}/{Repo}/pulls/{Number} failed with {Status}",
                owner, repo, number, response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var rawState = root.TryGetProperty("state", out var st)
            ? st.GetString() ?? "open" : "open";
        var merged = root.TryGetProperty("merged", out var mEl)
            && mEl.ValueKind == JsonValueKind.True;
        DateTimeOffset? mergedAt = null;
        if (root.TryGetProperty("merged_at", out var mAt)
            && mAt.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(mAt.GetString(), out var parsed))
        {
            mergedAt = parsed;
        }

        // GitHub's wire shape is `state` ∈ {open, closed} plus a
        // separate `merged` boolean. Collapse into "merged" when both
        // closed and merged so the rest of the codebase has a single
        // tri-state to reason about.
        var normalisedState = merged ? "merged" : rawState;

        var headBranch = root.TryGetProperty("head", out var head)
            && head.TryGetProperty("ref", out var refEl)
                ? refEl.GetString() ?? string.Empty
                : string.Empty;

        return new PullRequestStatusInfo(normalisedState, merged, mergedAt, headBranch);
    }
}
