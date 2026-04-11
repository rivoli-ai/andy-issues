// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Services;

/// <summary>
/// Orchestrates the "push from sandbox and open a pull request" flow. The push itself is
/// executed inside the caller's sandbox container via andy-containers (<see cref="IContainersClient"/>);
/// PR creation is then dispatched to the appropriate provider client based on the repository's
/// <see cref="RepositoryProvider"/>.
/// </summary>
public class PullRequestService : IPullRequestService
{
    private readonly AppDbContext _db;
    private readonly IContainersClient _containers;
    private readonly IGitHubClient _github;
    private readonly IAzureDevOpsClient _azureDevOps;
    private readonly ILogger<PullRequestService> _logger;

    public PullRequestService(
        AppDbContext db,
        IContainersClient containers,
        IGitHubClient github,
        IAzureDevOpsClient azureDevOps,
        ILogger<PullRequestService> logger)
    {
        _db = db;
        _containers = containers;
        _github = github;
        _azureDevOps = azureDevOps;
        _logger = logger;
    }

    public async Task<PullRequestResult> CreateFromSandboxAsync(
        Guid repositoryId,
        CreatePullRequestFromSandboxRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var repo = await _db.Repositories
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
        if (repo is null)
            return new PullRequestResult(PullRequestOutcome.NotFound, null, null);

        // Owner-only (shares don't unlock PR creation from this repo).
        if (repo.OwnerUserId != userId)
            return new PullRequestResult(PullRequestOutcome.Forbidden, null, null);

        var sandbox = await _db.Sandboxes
            .FirstOrDefaultAsync(s => s.Id == request.SandboxId, ct);
        if (sandbox is null || sandbox.RepositoryId != repositoryId)
            return new PullRequestResult(PullRequestOutcome.NotFound, null, null);
        if (sandbox.OwnerUserId != userId)
            return new PullRequestResult(PullRequestOutcome.Forbidden, null, null);

        var pushCommand = $"git -C /workspace push -u origin {Sanitize(request.SourceBranch)}";
        try
        {
            var exec = await _containers.ExecAsync(sandbox.ContainerId, pushCommand, ct);
            if (exec.ExitCode != 0)
            {
                _logger.LogWarning(
                    "git push failed in sandbox {SandboxId} (exit={ExitCode}): {StdErr}",
                    sandbox.Id, exec.ExitCode, exec.StdErr);
                return new PullRequestResult(
                    PullRequestOutcome.PushFailed,
                    null,
                    exec.StdErr ?? $"git push exited with {exec.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "git push exec threw in sandbox {SandboxId}.", sandbox.Id);
            return new PullRequestResult(PullRequestOutcome.PushFailed, null, ex.Message);
        }

        string? prUrl;
        switch (repo.Provider)
        {
            case RepositoryProvider.GitHub:
                prUrl = await CreateGitHubPullRequestAsync(repo.CloneUrl, userId, request, ct);
                break;
            case RepositoryProvider.AzureDevOps:
                prUrl = await CreateAzureDevOpsPullRequestAsync(repo.CloneUrl, repo.ExternalId, userId, request, ct);
                break;
            default:
                return new PullRequestResult(
                    PullRequestOutcome.UnsupportedProvider,
                    null,
                    $"Provider {repo.Provider} is not supported for PR creation.");
        }

        if (string.IsNullOrWhiteSpace(prUrl))
            return new PullRequestResult(PullRequestOutcome.ProviderFailed, null, "Provider returned no URL.");

        if (request.StoryId is Guid storyId)
        {
            var story = await _db.UserStories.FirstOrDefaultAsync(s => s.Id == storyId, ct);
            if (story is not null)
            {
                story.PullRequestUrl = prUrl;
                story.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }

        return new PullRequestResult(PullRequestOutcome.Created, prUrl, null);
    }

    private async Task<string?> CreateGitHubPullRequestAsync(
        string cloneUrl,
        string userId,
        CreatePullRequestFromSandboxRequest request,
        CancellationToken ct)
    {
        if (!TryParseGitHubOwnerRepo(cloneUrl, out var owner, out var repoName))
        {
            _logger.LogWarning("Cannot parse GitHub owner/repo from clone URL {CloneUrl}.", cloneUrl);
            return null;
        }

        var provider = await _db.LinkedProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OwnerUserId == userId && p.Provider == LinkedProviderKind.GitHub, ct);
        if (provider is null)
        {
            _logger.LogWarning("No GitHub linked provider for user {UserId}.", userId);
            return null;
        }

        var info = await _github.CreatePullRequestAsync(
            owner, repoName,
            request.Title, request.Description,
            request.SourceBranch, request.TargetBranch,
            provider.AccessToken, ct);
        return info?.Url;
    }

    private async Task<string?> CreateAzureDevOpsPullRequestAsync(
        string cloneUrl,
        string? externalId,
        string userId,
        CreatePullRequestFromSandboxRequest request,
        CancellationToken ct)
    {
        if (!BacklogAzureDevOpsSyncService.TryParseOrgProject(cloneUrl, out var org, out var project))
        {
            _logger.LogWarning("Cannot parse Azure DevOps org/project from clone URL {CloneUrl}.", cloneUrl);
            return null;
        }

        if (string.IsNullOrWhiteSpace(externalId))
        {
            _logger.LogWarning("Azure DevOps repository is missing ExternalId — cannot target a PR create call.");
            return null;
        }

        var provider = await _db.LinkedProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OwnerUserId == userId && p.Provider == LinkedProviderKind.AzureDevOps, ct);
        if (provider is null)
        {
            _logger.LogWarning("No Azure DevOps linked provider for user {UserId}.", userId);
            return null;
        }

        var info = await _azureDevOps.CreatePullRequestAsync(
            org, project, externalId,
            request.Title, request.Description,
            request.SourceBranch, request.TargetBranch,
            provider.AccessToken, ct);
        return info?.Url;
    }

    public static bool TryParseGitHubOwnerRepo(string cloneUrl, out string owner, out string repo)
    {
        owner = repo = string.Empty;
        if (string.IsNullOrWhiteSpace(cloneUrl)) return false;
        if (!Uri.TryCreate(cloneUrl, UriKind.Absolute, out var uri)) return false;
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return false;
        owner = segments[0];
        repo = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[1][..^4]
            : segments[1];
        return true;
    }

    private static string Sanitize(string branch)
    {
        // Refuse anything that could break out of the argument list into shell metacharacters.
        // Git branch names can't contain most of these anyway; this is defense in depth because
        // the push command is run via a remote exec.
        foreach (var ch in branch)
        {
            if (char.IsLetterOrDigit(ch)) continue;
            if (ch is '/' or '-' or '_' or '.' or '+') continue;
            throw new ArgumentException($"Illegal character in branch name: {ch}", nameof(branch));
        }
        return branch;
    }
}
