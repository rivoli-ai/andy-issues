// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.RegularExpressions;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Services;

/// <summary>
/// Imports GitHub issues into the local epic / feature / story
/// hierarchy. Intended to be called from an HTTP controller after the
/// user connects a GitHub repo and wants their existing backlog
/// (issues labelled <c>type:epic</c> / <c>type:feature</c> /
/// <c>type:story</c>) to show up without retyping everything.
/// </summary>
public class BacklogGitHubImportService : IBacklogGitHubImportService
{
    /// <summary>
    /// Prefix for ExternalId values written by this importer. Keeps
    /// the namespace from colliding with the Azure DevOps work item
    /// id (which lives on its own column) or future importers.
    /// </summary>
    internal const string ExternalIdPrefix = "gh:";

    /// <summary>Synthetic bucket for items that couldn't be parented via task lists.</summary>
    internal const string UncategorizedExternalId = ExternalIdPrefix + "uncategorized";

    // Repos vary wildly in labeling convention: rivoli-ai/conductor uses
    // strict `type:*` prefixes, rivoli-ai/andy-agents uses bare
    // `feature` / `documentation`, many repos are untyped entirely. The
    // classifier checks each set in order; anything that matches none
    // of these falls through to the default ("story") bucket so no
    // non-PR issue is ever silently dropped. Matching is case-
    // insensitive (see IssueType.Classify).
    internal static readonly IReadOnlyList<string> EpicLabels = new[]
    {
        "type:epic", "epic"
    };
    internal static readonly IReadOnlyList<string> FeatureLabels = new[]
    {
        "type:feature", "feature"
    };
    internal static readonly IReadOnlyList<string> StoryLabels = new[]
    {
        "type:story", "story", "user-story", "user story"
    };

    private static readonly Regex TaskListRefRegex = new(
        @"^\s*[-*]\s*\[[ xX]\]\s*#(\d+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Fast-path env var to pick up a GitHub PAT when no
    /// <see cref="Domain.Entities.LinkedProvider"/> row is registered.
    /// Temporary bridge (tracked as issue #76) until the andy-settings
    /// backed flow in Conductor ships.
    /// </summary>
    internal const string EnvironmentPatVariable = "GITHUB_PAT";

    private readonly AppDbContext _db;
    private readonly IGitHubClient _gitHubClient;
    private readonly IRepositoryAccessGuard _guard;
    private readonly ISecretStore _secretStore;
    private readonly IBacklogSequenceAllocator _sequence;
    private readonly ILogger<BacklogGitHubImportService> _logger;
    private readonly Func<string, string?> _environmentReader;

    public BacklogGitHubImportService(
        AppDbContext db,
        IGitHubClient gitHubClient,
        IRepositoryAccessGuard guard,
        ISecretStore secretStore,
        IBacklogSequenceAllocator sequence,
        ILogger<BacklogGitHubImportService> logger,
        Func<string, string?>? environmentReader = null)
    {
        _db = db;
        _gitHubClient = gitHubClient;
        _guard = guard;
        _secretStore = secretStore;
        _sequence = sequence;
        _logger = logger;
        _environmentReader = environmentReader ?? Environment.GetEnvironmentVariable;
    }

    public async Task<SyncResult?> ImportAsync(
        Guid repositoryId,
        string userId,
        CancellationToken ct = default)
    {
        if (!await _guard.CanViewAsync(repositoryId, userId, ct))
            return null;

        var repo = await _db.Repositories.FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
        if (repo is null) return null;

        if (repo.Provider != RepositoryProvider.GitHub)
            return new SyncResult(0, 0, 0, new[] { "Repository is not linked to GitHub." });

        if (!TryParseOwnerRepo(repo.CloneUrl, out var owner, out var repoName))
            return new SyncResult(0, 0, 0, new[] { $"Cannot derive GitHub owner/repo from clone URL '{repo.CloneUrl}'." });

        // A linked PAT is preferred (higher rate limit, private-repo
        // visibility) but not required — public repos work
        // anonymously. If no provider is registered, we still attempt
        // the call with a null token so first-click "Sync from GitHub"
        // produces data for public repos without any extra setup.
        var provider = await _db.LinkedProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OwnerUserId == userId
                && p.Provider == LinkedProviderKind.GitHub, ct);
        string? accessToken = null;
        if (provider is not null)
        {
            accessToken = await _secretStore.ResolveAsync(provider.AccessToken, ct) ?? provider.AccessToken;
        }

        if (string.IsNullOrEmpty(accessToken))
        {
            // Fast-path fallback (issue #76): pick up a PAT from the
            // GITHUB_PAT env var when no LinkedProvider is registered.
            // Conductor forwards its CONDUCTOR_GITHUB_TOKEN / GITHUB_TOKEN
            // into this variable when launching the embedded service.
            // User-registered LinkedProviders always win over the env
            // var because the check above sets `accessToken` first.
            // Deprecated once conductor#540 (Settings UI) lands.
            var envPat = _environmentReader(EnvironmentPatVariable);
            if (!string.IsNullOrEmpty(envPat))
            {
                _logger.LogInformation(
                    "Using {EnvVar} env-var fallback for {Owner}/{Repo} (no LinkedProvider).",
                    EnvironmentPatVariable, owner, repoName);
                accessToken = envPat;
            }
        }

        IReadOnlyList<GitHubIssueInfo> issues;
        try
        {
            // `string` parameter on the interface doesn't allow null,
            // so pass an empty string when no PAT is linked — the
            // implementation skips the Authorization header on empty.
            issues = await _gitHubClient.ListIssuesAsync(owner, repoName, accessToken ?? string.Empty, ct);
        }
        catch (GitHubApiException ex)
        {
            // Typed exception — message already phrased for the user
            // ("Repository is not publicly accessible — link a PAT",
            // rate-limit hints, etc.).
            return new SyncResult(0, 0, 0, new[] { ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GitHub issue listing failed for {Owner}/{Repo}", owner, repoName);
            return new SyncResult(0, 0, 0, new[] { $"GitHub issue listing failed: {ex.Message}" });
        }

        return await ImportIssuesAsync(repo.Id, issues, ct);
    }

    /// <summary>
    /// Core logic separated from the HTTP/token plumbing so tests can
    /// feed a pre-built issue list straight in.
    /// </summary>
    internal async Task<SyncResult> ImportIssuesAsync(
        Guid repositoryId,
        IReadOnlyList<GitHubIssueInfo> issues,
        CancellationToken ct)
    {
        // Classify.
        var epicIssues = new List<GitHubIssueInfo>();
        var featureIssues = new List<GitHubIssueInfo>();
        var storyIssues = new List<GitHubIssueInfo>();
        int pullRequestsSkipped = 0;

        foreach (var issue in issues)
        {
            if (issue.IsPullRequest) { pullRequestsSkipped++; continue; }

            switch (ClassifyIssue(issue.Labels))
            {
                case IssueType.Epic: epicIssues.Add(issue); break;
                case IssueType.Feature: featureIssues.Add(issue); break;
                case IssueType.Story: storyIssues.Add(issue); break;
            }
        }

        // Hierarchy inference: feature# -> epic#, story# -> feature#.
        // First-match wins so a feature showing up under two epics stays with the earliest.
        var featureToEpic = new Dictionary<int, int>();
        foreach (var epic in epicIssues)
        {
            foreach (var childNumber in ParseTaskListReferences(epic.Body))
            {
                if (!featureToEpic.ContainsKey(childNumber))
                    featureToEpic[childNumber] = epic.Number;
            }
        }
        var storyToFeature = new Dictionary<int, int>();
        foreach (var feature in featureIssues)
        {
            foreach (var childNumber in ParseTaskListReferences(feature.Body))
            {
                if (!storyToFeature.ContainsKey(childNumber))
                    storyToFeature[childNumber] = feature.Number;
            }
        }

        // Ensure the Uncategorized bucket exists so orphans have a parent.
        var (uncategorizedEpic, uncategorizedFeature) =
            await EnsureUncategorizedAsync(repositoryId, ct);

        int added = 0, updated = 0;
        var errors = new List<string>();

        // #670 Bug 2: re-home rows whose classification flipped since
        // the last sync. Each non-PR issue's CURRENT classification
        // (from its labels / type) is computed above; if the row
        // already exists in the WRONG entity table (e.g. an Epic that
        // was relabelled `type:story` upstream), we delete the old
        // row here so the upsert loop below creates the row in the
        // correct table. Cascade-delete clears stale children; the
        // correct children are recreated by the upsert pass since we
        // ran the full issue list.
        await RehomeReclassifiedItemsAsync(
            repositoryId,
            epicIssues, featureIssues, storyIssues,
            ct);

        // Upsert epics.
        var epicByNumber = new Dictionary<int, Epic>();
        foreach (var issue in epicIssues)
        {
            var epic = await UpsertEpicAsync(repositoryId, issue, ct);
            epicByNumber[issue.Number] = epic.entity;
            if (epic.added) added++; else updated++;
        }

        // Upsert features.
        var featureByNumber = new Dictionary<int, Feature>();
        foreach (var issue in featureIssues)
        {
            Guid parentEpicId = featureToEpic.TryGetValue(issue.Number, out var epicNumber)
                && epicByNumber.TryGetValue(epicNumber, out var parent)
                    ? parent.Id
                    : uncategorizedEpic.Id;

            var feature = await UpsertFeatureAsync(parentEpicId, issue, ct);
            featureByNumber[issue.Number] = feature.entity;
            if (feature.added) added++; else updated++;
        }

        // Upsert stories.
        foreach (var issue in storyIssues)
        {
            Guid parentFeatureId = storyToFeature.TryGetValue(issue.Number, out var featureNumber)
                && featureByNumber.TryGetValue(featureNumber, out var parent)
                    ? parent.Id
                    : uncategorizedFeature.Id;

            try
            {
                var story = await UpsertStoryAsync(parentFeatureId, issue, ct);
                if (story.added) added++; else updated++;
            }
            catch (InvalidOperationException ex)
            {
                // Raised by UserStory.SetStatus on an illegal transition
                // (e.g. a locally-Done story re-opened upstream). Skip
                // with an error rather than abort the whole import.
                errors.Add($"#{issue.Number}: {ex.Message}");
            }
        }

        await _db.SaveChangesAsync(ct);
        return new SyncResult(added, updated, pullRequestsSkipped, errors);
    }

    /// <summary>
    /// The three buckets an issue can land in. Every non-PR issue
    /// classifies as *something* — the fallback for unlabeled issues
    /// is Story, on the theory that the user cares about the work
    /// item even if nobody has triaged it yet.
    /// </summary>
    public enum IssueType { Epic, Feature, Story }

    /// <summary>
    /// Classifies an issue by its labels. Epic wins if any epic label
    /// matches (incl. bare <c>epic</c> and <c>type:epic</c>); feature
    /// wins next; otherwise story — explicit <c>story</c> label or
    /// unlabeled fallback.
    /// </summary>
    public static IssueType ClassifyIssue(IReadOnlyList<string> labels)
    {
        var normalized = new HashSet<string>(
            labels.Select(l => l.Trim().ToLowerInvariant()));

        if (EpicLabels.Any(l => normalized.Contains(l))) return IssueType.Epic;
        if (FeatureLabels.Any(l => normalized.Contains(l))) return IssueType.Feature;
        // Story is the catch-all: explicit story label OR unlabeled.
        return IssueType.Story;
    }

    private async Task<(Epic entity, bool added)> UpsertEpicAsync(
        Guid repositoryId, GitHubIssueInfo issue, CancellationToken ct)
    {
        var externalId = ExternalIdPrefix + issue.Number;
        var existing = await _db.Epics
            .FirstOrDefaultAsync(e => e.RepositoryId == repositoryId && e.ExternalId == externalId, ct);

        if (existing is null)
        {
            var epic = new Epic
            {
                Id = Guid.NewGuid(),
                Seq = await _sequence.AllocateAsync(BacklogEntityType.Epic, ct),
                RepositoryId = repositoryId,
                Title = issue.Title,
                Description = issue.Body,
                Order = issue.Number,
                ExternalId = externalId,
                Labels = issue.Labels.ToList(),
                GitHubType = issue.Type
            };
            _db.Epics.Add(epic);
            return (epic, true);
        }

        existing.Title = issue.Title;
        existing.Description = issue.Body;
        existing.Order = issue.Number;
        existing.Labels = issue.Labels.ToList();
        existing.GitHubType = issue.Type;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        return (existing, false);
    }

    private async Task<(Feature entity, bool added)> UpsertFeatureAsync(
        Guid epicId, GitHubIssueInfo issue, CancellationToken ct)
    {
        var externalId = ExternalIdPrefix + issue.Number;
        var existing = await _db.Features
            .FirstOrDefaultAsync(f => f.ExternalId == externalId, ct);

        if (existing is null)
        {
            var feature = new Feature
            {
                Id = Guid.NewGuid(),
                Seq = await _sequence.AllocateAsync(BacklogEntityType.Feature, ct),
                EpicId = epicId,
                Title = issue.Title,
                Description = issue.Body,
                Order = issue.Number,
                ExternalId = externalId,
                Labels = issue.Labels.ToList(),
                GitHubType = issue.Type
            };
            _db.Features.Add(feature);
            return (feature, true);
        }

        existing.EpicId = epicId;
        existing.Title = issue.Title;
        existing.Description = issue.Body;
        existing.Order = issue.Number;
        existing.Labels = issue.Labels.ToList();
        existing.GitHubType = issue.Type;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        return (existing, false);
    }

    private async Task<(UserStory entity, bool added)> UpsertStoryAsync(
        Guid featureId, GitHubIssueInfo issue, CancellationToken ct)
    {
        var externalId = ExternalIdPrefix + issue.Number;
        var existing = await _db.UserStories
            .FirstOrDefaultAsync(s => s.ExternalId == externalId, ct);

        var targetStatus = StatusFromIssueState(issue.State);

        if (existing is null)
        {
            var story = new UserStory
            {
                Id = Guid.NewGuid(),
                Seq = await _sequence.AllocateAsync(BacklogEntityType.Story, ct),
                FeatureId = featureId,
                Title = issue.Title,
                Description = issue.Body,
                Order = issue.Number,
                ExternalId = externalId,
                Labels = issue.Labels.ToList(),
                GitHubType = issue.Type
            };
            if (targetStatus != UserStoryStatus.Draft)
                story.SetStatus(targetStatus);
            _db.UserStories.Add(story);
            return (story, true);
        }

        existing.FeatureId = featureId;
        existing.Title = issue.Title;
        existing.Description = issue.Body;
        existing.Order = issue.Number;
        existing.Labels = issue.Labels.ToList();
        existing.GitHubType = issue.Type;
        // Only advance status forward from the remote side — never
        // overwrite an in-progress local story with Draft just because
        // the GitHub issue is still open. Done is the one signal we
        // trust from GitHub's side ("closed" is unambiguous).
        if (targetStatus == UserStoryStatus.Done && existing.Status != UserStoryStatus.Done)
            existing.SetStatus(UserStoryStatus.Done);
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        return (existing, false);
    }

    /// <summary>
    /// #670 Bug 2 — classification re-home pass.
    ///
    /// For each issue in the current sync batch, checks whether the row
    /// already exists in a DIFFERENT entity table than its new
    /// classification predicts. If so, deletes the old row so the
    /// normal upsert loop creates the row in the correct table. The
    /// row's hierarchy is re-inferred fresh on every sync (from the
    /// current GitHub task-list references), so orphaned children
    /// from a cascade-delete get reparented in the same pass.
    ///
    /// Example: an issue was labelled <c>type:feature</c> on first
    /// sync and stored in <c>Features</c>. Upstream it's relabelled
    /// <c>type:epic</c>. On the next sync, <see cref="ClassifyIssue"/>
    /// returns Epic, this method detects the row in Features with
    /// that <c>ExternalId</c>, deletes it, and the Epic upsert loop
    /// creates the new Epic row.
    /// </summary>
    private async Task RehomeReclassifiedItemsAsync(
        Guid repositoryId,
        List<GitHubIssueInfo> epicIssues,
        List<GitHubIssueInfo> featureIssues,
        List<GitHubIssueInfo> storyIssues,
        CancellationToken ct)
    {
        // Epics: delete any existing Feature or Story row that now
        // classifies as Epic.
        foreach (var issue in epicIssues)
        {
            var externalId = ExternalIdPrefix + issue.Number;

            var wrongFeature = await _db.Features
                .FirstOrDefaultAsync(f => f.ExternalId == externalId, ct);
            if (wrongFeature is not null) _db.Features.Remove(wrongFeature);

            var wrongStory = await _db.UserStories
                .FirstOrDefaultAsync(s => s.ExternalId == externalId, ct);
            if (wrongStory is not null) _db.UserStories.Remove(wrongStory);
        }

        // Features: delete any existing Epic or Story row that now
        // classifies as Feature.
        foreach (var issue in featureIssues)
        {
            var externalId = ExternalIdPrefix + issue.Number;

            var wrongEpic = await _db.Epics
                .FirstOrDefaultAsync(
                    e => e.RepositoryId == repositoryId && e.ExternalId == externalId,
                    ct);
            if (wrongEpic is not null) _db.Epics.Remove(wrongEpic);

            var wrongStory = await _db.UserStories
                .FirstOrDefaultAsync(s => s.ExternalId == externalId, ct);
            if (wrongStory is not null) _db.UserStories.Remove(wrongStory);
        }

        // Stories: delete any existing Epic or Feature row that now
        // classifies as Story.
        foreach (var issue in storyIssues)
        {
            var externalId = ExternalIdPrefix + issue.Number;

            var wrongEpic = await _db.Epics
                .FirstOrDefaultAsync(
                    e => e.RepositoryId == repositoryId && e.ExternalId == externalId,
                    ct);
            if (wrongEpic is not null) _db.Epics.Remove(wrongEpic);

            var wrongFeature = await _db.Features
                .FirstOrDefaultAsync(f => f.ExternalId == externalId, ct);
            if (wrongFeature is not null) _db.Features.Remove(wrongFeature);
        }

        // Flush the deletes so the upsert loop's `FirstOrDefaultAsync`
        // queries don't see the to-be-deleted rows in the tracked
        // change set.
        if (_db.ChangeTracker.HasChanges())
            await _db.SaveChangesAsync(ct);
    }

    private async Task<(Epic epic, Feature feature)> EnsureUncategorizedAsync(
        Guid repositoryId, CancellationToken ct)
    {
        var epic = await _db.Epics.FirstOrDefaultAsync(
            e => e.RepositoryId == repositoryId && e.ExternalId == UncategorizedExternalId, ct);
        if (epic is null)
        {
            epic = new Epic
            {
                Id = Guid.NewGuid(),
                Seq = await _sequence.AllocateAsync(BacklogEntityType.Epic, ct),
                RepositoryId = repositoryId,
                Title = "Uncategorized",
                Description = "Orphan epics/features/stories imported from GitHub that couldn't be parented via task-list references.",
                Order = int.MaxValue,
                ExternalId = UncategorizedExternalId
            };
            _db.Epics.Add(epic);
            await _db.SaveChangesAsync(ct);
        }

        var feature = await _db.Features.FirstOrDefaultAsync(
            f => f.EpicId == epic.Id && f.ExternalId == UncategorizedExternalId, ct);
        if (feature is null)
        {
            feature = new Feature
            {
                Id = Guid.NewGuid(),
                Seq = await _sequence.AllocateAsync(BacklogEntityType.Feature, ct),
                EpicId = epic.Id,
                Title = "Uncategorized",
                Description = "Orphan stories with no matching feature in the imported task-lists.",
                Order = int.MaxValue,
                ExternalId = UncategorizedExternalId
            };
            _db.Features.Add(feature);
            await _db.SaveChangesAsync(ct);
        }

        return (epic, feature);
    }

    public static UserStoryStatus StatusFromIssueState(string issueState) =>
        issueState.Equals("closed", StringComparison.OrdinalIgnoreCase)
            ? UserStoryStatus.Done
            : UserStoryStatus.Draft;

    /// <summary>
    /// Extracts <c>#123</c>-style issue references that appear on
    /// lines starting with a markdown task-list marker.
    /// </summary>
    public static IEnumerable<int> ParseTaskListReferences(string? body)
    {
        if (string.IsNullOrEmpty(body)) yield break;
        foreach (Match match in TaskListRefRegex.Matches(body))
        {
            if (int.TryParse(match.Groups[1].ValueSpan, out var number))
                yield return number;
        }
    }

    /// <summary>
    /// Extracts the <c>owner/repo</c> pair from an HTTPS or SSH GitHub
    /// clone URL. Supports <c>https://github.com/o/r(.git)?</c> and
    /// <c>git@github.com:o/r(.git)?</c>.
    /// </summary>
    public static bool TryParseOwnerRepo(string cloneUrl, out string owner, out string repo)
    {
        owner = repo = string.Empty;
        if (string.IsNullOrWhiteSpace(cloneUrl)) return false;

        // SSH form: git@github.com:owner/repo(.git)?
        if (cloneUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            var colon = cloneUrl.IndexOf(':');
            if (colon < 0) return false;
            var hostPart = cloneUrl[4..colon];
            if (!hostPart.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
            var path = cloneUrl[(colon + 1)..];
            return SplitOwnerRepo(path, out owner, out repo);
        }

        if (!Uri.TryCreate(cloneUrl, UriKind.Absolute, out var uri)) return false;
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
        return SplitOwnerRepo(uri.AbsolutePath.TrimStart('/'), out owner, out repo);
    }

    private static bool SplitOwnerRepo(string path, out string owner, out string repo)
    {
        owner = repo = string.Empty;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return false;
        owner = segments[0];
        repo = segments[1];
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repo = repo[..^4];
        return owner.Length > 0 && repo.Length > 0;
    }
}
