// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text;
using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Services;

/// <summary>
/// LLM-driven re-categorization of the synthetic "Uncategorized"
/// bucket. Collects the features/stories the GitHub importer parked
/// under the <c>gh:uncategorized</c> epic/feature, asks the repo's
/// configured LLM for an epic → feature → story placement (existing
/// parents preferred, new epics/features invented when needed),
/// applies it locally, and — when <c>applyToGitHub</c> — writes the
/// classification back as labels, created issues, and native
/// sub-issue links.
///
/// LLM dispatch goes through <see cref="LlmChatCompletion"/> so the
/// Anthropic Messages API is handled correctly (provider-aware, like
/// <see cref="BacklogAiService"/> — NOT the chat/completions-only call
/// in <c>DraftBacklogGenerator</c>).
/// </summary>
public class BacklogRecategorizeService : IBacklogRecategorizeService
{
    private const string UncategorizedExternalId = BacklogGitHubImportService.UncategorizedExternalId;
    private const string ExternalIdPrefix = BacklogGitHubImportService.ExternalIdPrefix;

    private const string EpicLabel = "type:epic";
    private const string FeatureLabel = "type:feature";
    private const string StoryLabel = "type:story";

    private const string SystemPrompt =
        "You are a software architect organizing a product backlog. " +
        "You classify backlog items into an epic > feature > story hierarchy. " +
        "Respond with a single strict JSON object and nothing else.";

    private readonly AppDbContext _db;
    private readonly IGitHubClient _gitHubClient;
    private readonly IRepositoryAccessGuard _guard;
    private readonly ISecretStore _secretStore;
    private readonly IBacklogSequenceAllocator _sequence;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BacklogRecategorizeService> _logger;
    private readonly Func<string, string?> _environmentReader;

    public BacklogRecategorizeService(
        AppDbContext db,
        IGitHubClient gitHubClient,
        IRepositoryAccessGuard guard,
        ISecretStore secretStore,
        IBacklogSequenceAllocator sequence,
        IHttpClientFactory httpClientFactory,
        ILogger<BacklogRecategorizeService> logger,
        Func<string, string?>? environmentReader = null)
    {
        _db = db;
        _gitHubClient = gitHubClient;
        _guard = guard;
        _secretStore = secretStore;
        _sequence = sequence;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _environmentReader = environmentReader ?? Environment.GetEnvironmentVariable;
    }

    public async Task<RecategorizeResult?> RecategorizeAsync(
        Guid repositoryId,
        string userId,
        bool applyToGitHub,
        CancellationToken ct = default)
    {
        // Same access rule as the importer's sync path: CanView,
        // null → controller 404. Ownership is deliberately NOT
        // required (matching sync-github-issues, which this endpoint
        // is the follow-up step to).
        if (!await _guard.CanViewAsync(repositoryId, userId, ct))
            return null;

        var repo = await _db.Repositories
            .Include(r => r.LlmSetting)
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
        if (repo is null) return null;

        // ── Input set: everything parked under the Uncategorized
        // buckets. Features under the uncategorized EPIC (excluding
        // the synthetic uncategorized feature itself) and stories
        // under the uncategorized FEATURE.
        var inputFeatures = await _db.Features
            .Include(f => f.Epic)
            .Where(f => f.Epic.RepositoryId == repositoryId
                && f.Epic.ExternalId == UncategorizedExternalId
                && f.ExternalId != UncategorizedExternalId)
            .ToListAsync(ct);
        var inputStories = await _db.UserStories
            .Include(s => s.Feature)
            .Where(s => s.Feature.Epic.RepositoryId == repositoryId
                && s.Feature.ExternalId == UncategorizedExternalId)
            .ToListAsync(ct);

        if (inputFeatures.Count == 0 && inputStories.Count == 0)
            return new RecategorizeResult(
                RecategorizeOutcome.NothingToDo,
                Message: "No uncategorized items found — nothing to classify.");

        if (repo.LlmSetting is null)
            return new RecategorizeResult(
                RecategorizeOutcome.NoLlmSetting,
                Message: "Repository has no LLM setting configured. Link one via PATCH /api/repositories/{id}/llm-setting.");

        // ── Existing real parents (everything NOT in the synthetic
        // buckets) the LLM can assign items to. Synthetic per-epic
        // "Stories" features (gh:{n}/stories) are excluded — they are
        // importer plumbing, not parents the LLM should target.
        var existingEpics = await _db.Epics
            .Where(e => e.RepositoryId == repositoryId
                && e.ExternalId != UncategorizedExternalId)
            .ToListAsync(ct);
        var existingFeatures = await _db.Features
            .Include(f => f.Epic)
            .Where(f => f.Epic.RepositoryId == repositoryId
                && f.Epic.ExternalId != UncategorizedExternalId
                && f.ExternalId != UncategorizedExternalId)
            .ToListAsync(ct);
        existingFeatures = existingFeatures
            .Where(f => f.ExternalId is null || !f.ExternalId.EndsWith("/stories", StringComparison.Ordinal))
            .ToList();

        // ── LLM call
        var prompt = BuildPrompt(repo.Name, existingEpics, existingFeatures, inputFeatures, inputStories);
        string llmResponse;
        try
        {
            llmResponse = await LlmChatCompletion.CompleteAsync(
                _httpClientFactory,
                repo.LlmSetting,
                systemPrompt: SystemPrompt,
                userPrompt: prompt,
                maxTokens: 8192,
                temperature: 0.2,
                requestJsonObject: true,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Recategorize LLM call failed for repository {RepositoryId}.", repositoryId);
            return new RecategorizeResult(
                RecategorizeOutcome.LlmCallFailed,
                Message: $"LLM call failed: {ex.Message}");
        }

        RecategorizePlan plan;
        try
        {
            plan = ParsePlan(llmResponse);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Recategorize LLM response parse failed for repository {RepositoryId}.", repositoryId);
            return new RecategorizeResult(
                RecategorizeOutcome.ParseFailed,
                Message: $"Could not parse LLM response: {ex.Message}");
        }

        // ── Apply locally
        var errors = new List<string>();
        int classified = 0, epicsCreated = 0, featuresCreated = 0, storiesReparented = 0;
        int labelsApplied = 0, subIssuesLinked = 0, githubIssuesCreated = 0;

        // Ref lookup for EXISTING parents. Every epic/feature is
        // addressable by the canonical ref the prompt printed:
        // `existing:{ghNumber}` when it has one, plus the display-id
        // form (`existing:EPIC-5`) as a defensive alias.
        var epicRefs = new Dictionary<string, Epic>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in existingEpics)
        {
            if (TryParseGhNumber(e.ExternalId, out var n)) epicRefs[$"existing:{n}"] = e;
            epicRefs[$"existing:{e.DisplayId}"] = e;
        }
        var featureRefs = new Dictionary<string, Feature>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in existingFeatures)
        {
            if (TryParseGhNumber(f.ExternalId, out var n)) featureRefs[$"existing:{n}"] = f;
            featureRefs[$"existing:{f.DisplayId}"] = f;
        }

        // New epics first (features may parent under them).
        var createdEpics = new List<Epic>();
        var epicOrder = await NextEpicOrderAsync(repositoryId, ct);
        foreach (var pe in plan.Epics)
        {
            if (!pe.Ref.StartsWith("new:", StringComparison.OrdinalIgnoreCase))
                continue; // existing refs in the epics array are descriptive only
            if (epicRefs.ContainsKey(pe.Ref))
            {
                errors.Add($"{pe.Ref}: duplicate new-epic ref — ignored.");
                continue;
            }
            var epic = new Epic
            {
                Id = Guid.NewGuid(),
                Seq = await _sequence.AllocateAsync(BacklogEntityType.Epic, ct),
                RepositoryId = repositoryId,
                Title = pe.Title,
                Description = pe.Description,
                Order = epicOrder++
            };
            _db.Epics.Add(epic);
            epicRefs[pe.Ref] = epic;
            createdEpics.Add(epic);
            epicsCreated++;
        }

        // New features.
        var createdFeatures = new List<(Feature Feature, Epic ParentEpic)>();
        var featureOrderByEpic = new Dictionary<Guid, int>();
        async Task<int> NextFeatureOrderAsync(Epic epic)
        {
            if (!featureOrderByEpic.TryGetValue(epic.Id, out var order))
            {
                // Synthetic buckets pin Order near int.MaxValue so
                // they sort last — exclude them from the max.
                order = createdEpics.Contains(epic)
                    ? 0
                    : await _db.Features
                        .AsNoTracking()
                        .Where(f => f.EpicId == epic.Id
                            && (f.ExternalId == null
                                || (f.ExternalId != UncategorizedExternalId
                                    && !f.ExternalId.EndsWith("/stories"))))
                        .Select(f => (int?)f.Order)
                        .MaxAsync(ct) ?? 0;
            }
            featureOrderByEpic[epic.Id] = order + 1;
            return order + 1;
        }
        foreach (var pf in plan.Features)
        {
            if (!pf.Ref.StartsWith("new:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (featureRefs.ContainsKey(pf.Ref))
            {
                errors.Add($"{pf.Ref}: duplicate new-feature ref — ignored.");
                continue;
            }
            if (pf.EpicRef is null || !epicRefs.TryGetValue(pf.EpicRef, out var parentEpic))
            {
                errors.Add($"{pf.Ref}: unknown epicRef '{pf.EpicRef}' — feature not created.");
                continue;
            }
            var feature = new Feature
            {
                Id = Guid.NewGuid(),
                Seq = await _sequence.AllocateAsync(BacklogEntityType.Feature, ct),
                EpicId = parentEpic.Id,
                Title = pf.Title,
                Description = pf.Description,
                Order = await NextFeatureOrderAsync(parentEpic)
            };
            _db.Features.Add(feature);
            featureRefs[pf.Ref] = feature;
            createdFeatures.Add((feature, parentEpic));
            featuresCreated++;
        }

        // Assignments. Items are addressed by the key the prompt
        // printed: the `gh:{n}` ExternalId when present, else the
        // display id (STORY-7 / FEAT-3).
        var storyByKey = inputStories.ToDictionary(ItemKey, s => s, StringComparer.OrdinalIgnoreCase);
        var featureByKey = inputFeatures.ToDictionary(ItemKey, f => f, StringComparer.OrdinalIgnoreCase);
        var assignedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Successfully placed items, captured for the GitHub
        // write-back pass: the FINAL local row + role + final parent.
        var placedStories = new List<(UserStory Story, Feature Parent)>();
        var placedFeatures = new List<(Feature Feature, Epic Parent)>();
        var placedEpics = new List<Epic>();

        foreach (var a in plan.Assignments)
        {
            if (!assignedKeys.Add(a.Item))
            {
                errors.Add($"{a.Item}: duplicate assignment — ignored.");
                continue;
            }

            var role = a.Role.Trim().ToLowerInvariant();
            if (role is not ("story" or "feature" or "epic"))
            {
                errors.Add($"{a.Item}: unknown role '{a.Role}' — skipped.");
                continue;
            }

            if (storyByKey.TryGetValue(a.Item, out var story))
            {
                switch (role)
                {
                    case "story":
                        if (a.ParentRef is null || !featureRefs.TryGetValue(a.ParentRef, out var parentFeature))
                        {
                            errors.Add($"{a.Item}: unknown parentRef '{a.ParentRef}' — skipped.");
                            continue;
                        }
                        story.FeatureId = parentFeature.Id;
                        story.Feature = parentFeature;
                        story.UpdatedAt = DateTimeOffset.UtcNow;
                        placedStories.Add((story, parentFeature));
                        storiesReparented++;
                        classified++;
                        break;

                    case "feature":
                    {
                        if (a.ParentRef is null || !epicRefs.TryGetValue(a.ParentRef, out var parentEpic))
                        {
                            errors.Add($"{a.Item}: unknown parentRef '{a.ParentRef}' — skipped.");
                            continue;
                        }
                        // Table move mirroring RehomeReclassifiedItemsAsync:
                        // delete the story row, recreate as a Feature with
                        // ExternalId / labels / title / description preserved.
                        // Uncategorized stories have no children, so the
                        // conversion orphans nothing.
                        var converted = new Feature
                        {
                            Id = Guid.NewGuid(),
                            Seq = await _sequence.AllocateAsync(BacklogEntityType.Feature, ct),
                            EpicId = parentEpic.Id,
                            Title = story.Title,
                            Description = story.Description,
                            Order = story.Order,
                            ExternalId = story.ExternalId,
                            Labels = story.Labels.ToList(),
                            GitHubType = story.GitHubType
                        };
                        _db.Features.Add(converted);
                        _db.UserStories.Remove(story);
                        placedFeatures.Add((converted, parentEpic));
                        classified++;
                        break;
                    }

                    case "epic":
                    {
                        var converted = new Epic
                        {
                            Id = Guid.NewGuid(),
                            Seq = await _sequence.AllocateAsync(BacklogEntityType.Epic, ct),
                            RepositoryId = repositoryId,
                            Title = story.Title,
                            Description = story.Description,
                            Order = story.Order,
                            ExternalId = story.ExternalId,
                            Labels = story.Labels.ToList(),
                            GitHubType = story.GitHubType
                        };
                        _db.Epics.Add(converted);
                        _db.UserStories.Remove(story);
                        placedEpics.Add(converted);
                        classified++;
                        break;
                    }
                }
            }
            else if (featureByKey.TryGetValue(a.Item, out var inputFeature))
            {
                switch (role)
                {
                    case "feature":
                        if (a.ParentRef is null || !epicRefs.TryGetValue(a.ParentRef, out var parentEpic))
                        {
                            errors.Add($"{a.Item}: unknown parentRef '{a.ParentRef}' — skipped.");
                            continue;
                        }
                        inputFeature.EpicId = parentEpic.Id;
                        inputFeature.Epic = parentEpic;
                        inputFeature.UpdatedAt = DateTimeOffset.UtcNow;
                        placedFeatures.Add((inputFeature, parentEpic));
                        classified++;
                        break;

                    case "epic":
                    {
                        // A feature with child stories cannot be moved to
                        // the Epics table without orphaning (the FK cascade
                        // would delete the children). Refuse per-item.
                        if (await HasStoriesAsync(inputFeature, ct))
                        {
                            errors.Add($"{a.Item}: cannot convert a feature that has stories into an epic — skipped.");
                            continue;
                        }
                        var converted = new Epic
                        {
                            Id = Guid.NewGuid(),
                            Seq = await _sequence.AllocateAsync(BacklogEntityType.Epic, ct),
                            RepositoryId = repositoryId,
                            Title = inputFeature.Title,
                            Description = inputFeature.Description,
                            Order = inputFeature.Order,
                            ExternalId = inputFeature.ExternalId,
                            Labels = inputFeature.Labels.ToList(),
                            GitHubType = inputFeature.GitHubType
                        };
                        _db.Epics.Add(converted);
                        _db.Features.Remove(inputFeature);
                        placedEpics.Add(converted);
                        classified++;
                        break;
                    }

                    case "story":
                    {
                        if (a.ParentRef is null || !featureRefs.TryGetValue(a.ParentRef, out var parentFeature))
                        {
                            errors.Add($"{a.Item}: unknown parentRef '{a.ParentRef}' — skipped.");
                            continue;
                        }
                        if (await HasStoriesAsync(inputFeature, ct))
                        {
                            errors.Add($"{a.Item}: cannot convert a feature that has stories into a story — skipped.");
                            continue;
                        }
                        var converted = new UserStory
                        {
                            Id = Guid.NewGuid(),
                            Seq = await _sequence.AllocateAsync(BacklogEntityType.Story, ct),
                            FeatureId = parentFeature.Id,
                            Title = inputFeature.Title,
                            Description = inputFeature.Description,
                            Order = inputFeature.Order,
                            ExternalId = inputFeature.ExternalId,
                            Labels = inputFeature.Labels.ToList(),
                            GitHubType = inputFeature.GitHubType
                        };
                        _db.UserStories.Add(converted);
                        _db.Features.Remove(inputFeature);
                        placedStories.Add((converted, parentFeature));
                        classified++;
                        break;
                    }
                }
            }
            else
            {
                errors.Add($"{a.Item}: not an uncategorized item in this repository — skipped.");
            }
        }

        await _db.SaveChangesAsync(ct);

        // ── Optional GitHub write-back
        if (applyToGitHub)
        {
            var accessToken = await ResolveGitHubPatAsync(userId, ct);
            if (string.IsNullOrEmpty(accessToken))
            {
                errors.Add("No GitHub credential linked — recategorized locally only.");
            }
            else if (!BacklogGitHubImportService.TryParseOwnerRepo(repo.CloneUrl, out var owner, out var repoName))
            {
                errors.Add($"Cannot derive GitHub owner/repo from clone URL '{repo.CloneUrl}' — recategorized locally only.");
            }
            else
            {
                var counts = await ApplyToGitHubAsync(
                    owner, repoName, accessToken,
                    createdEpics, createdFeatures,
                    placedStories, placedFeatures, placedEpics,
                    errors, ct);
                githubIssuesCreated = counts.GithubIssuesCreated;
                labelsApplied = counts.LabelsApplied;
                subIssuesLinked = counts.SubIssuesLinked;

                await _db.SaveChangesAsync(ct);
            }
        }

        // ── Heal emptied synthetic buckets (shared with the importer).
        await BacklogGitHubImportService.CleanupEmptySyntheticBucketsAsync(_db, repositoryId, ct);

        return new RecategorizeResult(
            RecategorizeOutcome.Recategorized,
            Classified: classified,
            EpicsCreated: epicsCreated,
            FeaturesCreated: featuresCreated,
            StoriesReparented: storiesReparented,
            LabelsApplied: labelsApplied,
            SubIssuesLinked: subIssuesLinked,
            GithubIssuesCreated: githubIssuesCreated,
            Errors: errors);
    }

    // MARK: - GitHub write-back

    private async Task<(int GithubIssuesCreated, int LabelsApplied, int SubIssuesLinked)> ApplyToGitHubAsync(
        string owner,
        string repoName,
        string accessToken,
        List<Epic> createdEpics,
        List<(Feature Feature, Epic ParentEpic)> createdFeatures,
        List<(UserStory Story, Feature Parent)> placedStories,
        List<(Feature Feature, Epic Parent)> placedFeatures,
        List<Epic> placedEpics,
        List<string> errors,
        CancellationToken ct)
    {
        int githubIssuesCreated = 0, labelsApplied = 0, subIssuesLinked = 0;

        // number → database id map for sub-issue linking. One upfront
        // list call; a failure degrades sub-issue linking only.
        var idByNumber = new Dictionary<int, long>();
        try
        {
            var issues = await _gitHubClient.ListIssuesAsync(owner, repoName, accessToken, ct);
            foreach (var issue in issues)
            {
                if (!issue.IsPullRequest && issue.Id != 0)
                    idByNumber[issue.Number] = issue.Id;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            errors.Add($"GitHub issue listing failed — sub-issue links for existing issues skipped: {ex.Message}");
        }

        // New epics/features → real GitHub issues. Success stamps the
        // local row's ExternalId so future syncs upsert by number.
        foreach (var epic in createdEpics)
        {
            try
            {
                var created = await _gitHubClient.CreateIssueAsync(
                    owner, repoName, epic.Title, epic.Description,
                    new[] { EpicLabel }, accessToken, ct);
                epic.ExternalId = ExternalIdPrefix + created.Number;
                epic.Labels = MergedLabels(epic.Labels, EpicLabel);
                idByNumber[created.Number] = created.Id;
                githubIssuesCreated++;
            }
            catch (GitHubApiException ex)
            {
                errors.Add($"'{epic.Title}': {ex.Message}");
            }
        }
        foreach (var (feature, _) in createdFeatures)
        {
            try
            {
                var created = await _gitHubClient.CreateIssueAsync(
                    owner, repoName, feature.Title, feature.Description,
                    new[] { FeatureLabel }, accessToken, ct);
                feature.ExternalId = ExternalIdPrefix + created.Number;
                feature.Labels = MergedLabels(feature.Labels, FeatureLabel);
                idByNumber[created.Number] = created.Id;
                githubIssuesCreated++;
            }
            catch (GitHubApiException ex)
            {
                errors.Add($"'{feature.Title}': {ex.Message}");
            }
        }

        // Role labels on every classified item that maps to a real
        // GitHub issue. Skip when the row already carries the label.
        async Task ApplyLabelAsync(string? externalId, List<string> labels, string roleLabel)
        {
            if (!TryParseGhNumber(externalId, out var number)) return;
            if (labels.Any(l => string.Equals(l.Trim(), roleLabel, StringComparison.OrdinalIgnoreCase)))
                return;
            try
            {
                await _gitHubClient.AddLabelsAsync(
                    owner, repoName, number, new[] { roleLabel }, accessToken, ct);
                labels.Add(roleLabel);
                labelsApplied++;
            }
            catch (GitHubApiException ex)
            {
                errors.Add($"#{number}: {ex.Message}");
            }
        }

        foreach (var (story, _) in placedStories)
            await ApplyLabelAsync(story.ExternalId, story.Labels, StoryLabel);
        foreach (var (feature, _) in placedFeatures)
            await ApplyLabelAsync(feature.ExternalId, feature.Labels, FeatureLabel);
        foreach (var epic in placedEpics)
            await ApplyLabelAsync(epic.ExternalId, epic.Labels, EpicLabel);

        // Native sub-issue links for every placed parent-child pair
        // where BOTH sides resolved to real GitHub issue numbers
        // (including parents just created above).
        async Task LinkAsync(string? parentExternalId, string? childExternalId)
        {
            if (!TryParseGhNumber(parentExternalId, out var parentNumber)) return;
            if (!TryParseGhNumber(childExternalId, out var childNumber)) return;
            if (!idByNumber.TryGetValue(childNumber, out var childId) || childId == 0)
            {
                errors.Add($"#{childNumber}: could not resolve the GitHub issue id — sub-issue link skipped.");
                return;
            }
            try
            {
                await _gitHubClient.AddSubIssueAsync(
                    owner, repoName, parentNumber, childId, accessToken, ct);
                subIssuesLinked++;
            }
            catch (GitHubApiException ex)
            {
                errors.Add($"#{childNumber}: {ex.Message}");
            }
        }

        foreach (var (story, parent) in placedStories)
            await LinkAsync(parent.ExternalId, story.ExternalId);
        foreach (var (feature, parent) in placedFeatures)
            await LinkAsync(parent.ExternalId, feature.ExternalId);
        foreach (var (feature, parentEpic) in createdFeatures)
            await LinkAsync(parentEpic.ExternalId, feature.ExternalId);

        return (githubIssuesCreated, labelsApplied, subIssuesLinked);
    }

    /// <summary>
    /// PAT resolution, exactly like the importer: LinkedProvider →
    /// ISecretStore → <c>GITHUB_PAT</c> env-var fallback. Unlike the
    /// read path there is no anonymous mode — writes need a token.
    /// </summary>
    private async Task<string?> ResolveGitHubPatAsync(string userId, CancellationToken ct)
    {
        var provider = await _db.LinkedProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OwnerUserId == userId
                && p.Provider == LinkedProviderKind.GitHub, ct);
        if (provider is not null)
        {
            var resolved = await _secretStore.ResolveAsync(provider.AccessToken, ct) ?? provider.AccessToken;
            if (!string.IsNullOrEmpty(resolved)) return resolved;
        }
        return _environmentReader(BacklogGitHubImportService.EnvironmentPatVariable);
    }

    // MARK: - Prompt

    /// <summary>
    /// Builds the classification prompt: existing parents (with the
    /// refs the model must echo back), the uncategorized items (keyed
    /// by gh number when imported, display id when manual), and the
    /// strict JSON schema for the answer.
    /// </summary>
    internal static string BuildPrompt(
        string repoName,
        IReadOnlyList<Epic> existingEpics,
        IReadOnlyList<Feature> existingFeatures,
        IReadOnlyList<Feature> inputFeatures,
        IReadOnlyList<UserStory> inputStories)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"The repository \"{repoName}\" has backlog items that could not be categorized automatically.");
        sb.AppendLine("Assign EVERY uncategorized item below a role (epic, feature, or story) and a parent.");
        sb.AppendLine("Prefer existing parents when they fit; invent new epics/features only when none fits.");
        sb.AppendLine();

        sb.AppendLine("Existing epics (reference with the exact ref shown):");
        if (existingEpics.Count == 0) sb.AppendLine("  (none)");
        foreach (var e in existingEpics)
            sb.AppendLine($"  {ParentRef(e.ExternalId, e.DisplayId)} | {e.Title} | {Excerpt(e.Description, 120)}");
        sb.AppendLine();

        sb.AppendLine("Existing features (reference with the exact ref shown; parent epic noted):");
        if (existingFeatures.Count == 0) sb.AppendLine("  (none)");
        foreach (var f in existingFeatures)
            sb.AppendLine($"  {ParentRef(f.ExternalId, f.DisplayId)} | {f.Title} | under epic {ParentRef(f.Epic.ExternalId, f.Epic.DisplayId)} | {Excerpt(f.Description, 120)}");
        sb.AppendLine();

        sb.AppendLine("Uncategorized items (each MUST appear exactly once in \"assignments\", keyed by the id shown):");
        foreach (var f in inputFeatures)
            sb.AppendLine($"  {ItemKey(f)} | currently classified: feature | {f.Title} | labels: {string.Join(",", f.Labels)} | {Excerpt(f.Description, 400)}");
        foreach (var s in inputStories)
            sb.AppendLine($"  {ItemKey(s)} | currently classified: story | {s.Title} | labels: {string.Join(",", s.Labels)} | {Excerpt(s.Description, 400)}");
        sb.AppendLine();

        sb.AppendLine("""
            Return ONLY a JSON object with this exact structure:
            {
              "epics": [ { "ref": "new:E1", "title": "...", "description": "..." } ],
              "features": [ { "ref": "new:F1", "title": "...", "description": "...", "epicRef": "existing:32" } ],
              "assignments": [ { "item": "gh:12", "role": "story", "parentRef": "existing:45" } ]
            }
            Rules:
            - "epics" lists ONLY new epics to create, with refs "new:E1", "new:E2", ...
            - "features" lists ONLY new features to create, with refs "new:F1", ...; "epicRef" is an existing epic ref or a new epic ref.
            - every assignment's "item" is one of the uncategorized item ids above.
            - "role" is "story", "feature", or "epic" — the item's correct classification.
            - role "story": "parentRef" must be an existing feature ref or a new feature ref.
            - role "feature": "parentRef" must be an existing epic ref or a new epic ref.
            - role "epic": omit "parentRef".
            """);

        return sb.ToString();
    }

    private static string ParentRef(string? externalId, string displayId) =>
        TryParseGhNumber(externalId, out var n) ? $"existing:{n}" : $"existing:{displayId}";

    private static string Excerpt(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(no description)";
        var collapsed = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return collapsed.Length <= maxLength ? collapsed : collapsed[..maxLength] + "…";
    }

    // MARK: - Plan parsing

    internal record PlanEpic(string Ref, string Title, string? Description);
    internal record PlanFeature(string Ref, string Title, string? Description, string? EpicRef);
    internal record PlanAssignment(string Item, string Role, string? ParentRef);
    internal record RecategorizePlan(
        List<PlanEpic> Epics,
        List<PlanFeature> Features,
        List<PlanAssignment> Assignments);

    /// <summary>
    /// Parses the LLM's JSON answer. Structural problems (not an
    /// object, missing <c>assignments</c> array) throw — mapped to the
    /// ParseFailed outcome. Individual malformed entries are dropped
    /// here; semantically wrong entries (unknown refs/items) become
    /// per-item errors during apply.
    /// </summary>
    internal static RecategorizePlan ParsePlan(string raw)
    {
        var json = BacklogAiService.StripCodeFences(raw);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Response is not a JSON object.");
        if (!root.TryGetProperty("assignments", out var assignmentsEl)
            || assignmentsEl.ValueKind != JsonValueKind.Array)
            throw new JsonException("Response missing 'assignments' array.");

        var epics = new List<PlanEpic>();
        if (root.TryGetProperty("epics", out var epicsEl) && epicsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in epicsEl.EnumerateArray())
            {
                var r = GetString(el, "ref");
                var title = GetString(el, "title");
                if (r is null || title is null) continue;
                epics.Add(new PlanEpic(r.Trim(), title, GetString(el, "description")));
            }
        }

        var features = new List<PlanFeature>();
        if (root.TryGetProperty("features", out var featuresEl) && featuresEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in featuresEl.EnumerateArray())
            {
                var r = GetString(el, "ref");
                var title = GetString(el, "title");
                if (r is null || title is null) continue;
                features.Add(new PlanFeature(
                    r.Trim(), title, GetString(el, "description"), GetString(el, "epicRef")?.Trim()));
            }
        }

        var assignments = new List<PlanAssignment>();
        foreach (var el in assignmentsEl.EnumerateArray())
        {
            var item = GetString(el, "item");
            var role = GetString(el, "role");
            if (item is null || role is null) continue;
            assignments.Add(new PlanAssignment(
                item.Trim(), role.Trim(), GetString(el, "parentRef")?.Trim()));
        }

        return new RecategorizePlan(epics, features, assignments);
    }

    private static string? GetString(JsonElement el, string property) =>
        el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(property, out var v)
            && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

    // MARK: - Helpers

    /// <summary>
    /// Stable item key the prompt prints and assignments echo back:
    /// the <c>gh:{n}</c> ExternalId for imported items, the display id
    /// (<c>STORY-7</c> / <c>FEAT-3</c>) for manual ones.
    /// </summary>
    internal static string ItemKey(UserStory story) =>
        TryParseGhNumber(story.ExternalId, out _) ? story.ExternalId! : story.DisplayId;

    internal static string ItemKey(Feature feature) =>
        TryParseGhNumber(feature.ExternalId, out _) ? feature.ExternalId! : feature.DisplayId;

    internal static bool TryParseGhNumber(string? externalId, out int number)
    {
        number = 0;
        return externalId is not null
            && externalId.StartsWith(ExternalIdPrefix, StringComparison.Ordinal)
            && int.TryParse(externalId.AsSpan(ExternalIdPrefix.Length), out number)
            && number > 0;
    }

    private static List<string> MergedLabels(List<string> labels, string label)
    {
        if (!labels.Any(l => string.Equals(l.Trim(), label, StringComparison.OrdinalIgnoreCase)))
            labels.Add(label);
        return labels;
    }

    private Task<bool> HasStoriesAsync(Feature feature, CancellationToken ct) =>
        _db.UserStories.AnyAsync(s => s.FeatureId == feature.Id, ct);

    private async Task<int> NextEpicOrderAsync(Guid repositoryId, CancellationToken ct)
    {
        // The synthetic Uncategorized epic pins Order = int.MaxValue
        // so it sorts last — exclude it or the +1 below overflows.
        var max = await _db.Epics
            .AsNoTracking()
            .Where(e => e.RepositoryId == repositoryId
                && e.ExternalId != UncategorizedExternalId)
            .Select(e => (int?)e.Order)
            .MaxAsync(ct) ?? 0;
        return max + 1;
    }
}
