// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.PullRequests;

public abstract record ParsedPullRequestUrl;

public sealed record ParsedGitHubPullRequestUrl(
    string Owner,
    string Repo,
    int Number) : ParsedPullRequestUrl;

public sealed record ParsedAzureDevOpsPullRequestUrl(
    string Organization,
    string Project,
    string Repository,
    int Number) : ParsedPullRequestUrl;

// Lightweight URL classifier for PR endpoints (#88, #89, #90). Returns
// null when the URL is not a recognised GitHub or Azure DevOps PR URL
// — callers translate that into 400 Bad Request.
public static class PullRequestUrlParser
{
    public static ParsedPullRequestUrl? TryParse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        var host = uri.Host.ToLowerInvariant();
        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (host == "github.com" || host.EndsWith(".github.com", StringComparison.Ordinal))
            return TryParseGitHub(segments);

        if (host == "dev.azure.com")
            return TryParseAzureDevOpsCloud(segments);

        // Legacy {org}.visualstudio.com host. The org is in the host, so
        // the path layout drops the leading {organization} segment.
        if (host.EndsWith(".visualstudio.com", StringComparison.Ordinal))
        {
            var org = host.Substring(0, host.Length - ".visualstudio.com".Length);
            return TryParseAzureDevOpsLegacy(org, segments);
        }

        return null;
    }

    // github.com/{owner}/{repo}/pull/{n}
    private static ParsedPullRequestUrl? TryParseGitHub(string[] segments)
    {
        if (segments.Length < 4) return null;
        if (!string.Equals(segments[2], "pull", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!int.TryParse(segments[3], out var number) || number <= 0)
            return null;
        return new ParsedGitHubPullRequestUrl(segments[0], segments[1], number);
    }

    // dev.azure.com/{org}/{project}/_git/{repo}/pullrequest/{n}
    private static ParsedPullRequestUrl? TryParseAzureDevOpsCloud(string[] segments)
    {
        if (segments.Length < 6) return null;
        if (!string.Equals(segments[2], "_git", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!string.Equals(segments[4], "pullrequest", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!int.TryParse(segments[5], out var number) || number <= 0)
            return null;
        return new ParsedAzureDevOpsPullRequestUrl(
            segments[0], segments[1], segments[3], number);
    }

    // {org}.visualstudio.com/{project}/_git/{repo}/pullrequest/{n}
    private static ParsedPullRequestUrl? TryParseAzureDevOpsLegacy(string org, string[] segments)
    {
        if (segments.Length < 5) return null;
        if (!string.Equals(segments[1], "_git", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!string.Equals(segments[3], "pullrequest", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!int.TryParse(segments[4], out var number) || number <= 0)
            return null;
        return new ParsedAzureDevOpsPullRequestUrl(
            org, segments[0], segments[2], number);
    }
}
