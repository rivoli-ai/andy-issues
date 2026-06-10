// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Interfaces;

public enum RecategorizeOutcome
{
    /// <summary>Items were classified and applied locally (and optionally to GitHub).</summary>
    Recategorized = 0,

    /// <summary>The repository has no uncategorized items — nothing to classify.</summary>
    NothingToDo = 1,

    /// <summary>The repository has no LLM setting linked (controller → 400 no_llm_setting).</summary>
    NoLlmSetting = 2,

    /// <summary>The LLM HTTP call failed (controller → 502 llm_failed).</summary>
    LlmCallFailed = 3,

    /// <summary>The LLM response could not be parsed as the expected JSON (controller → 502 llm_failed).</summary>
    ParseFailed = 4
}

/// <summary>
/// Result of one recategorize run. Counts mirror the pinned HTTP
/// contract field-for-field:
/// <c>classified</c> / <c>epicsCreated</c> / <c>featuresCreated</c> /
/// <c>storiesReparented</c> / <c>labelsApplied</c> /
/// <c>subIssuesLinked</c> / <c>githubIssuesCreated</c> / <c>errors</c>.
/// </summary>
public record RecategorizeResult(
    RecategorizeOutcome Outcome,
    int Classified = 0,
    int EpicsCreated = 0,
    int FeaturesCreated = 0,
    int StoriesReparented = 0,
    int LabelsApplied = 0,
    int SubIssuesLinked = 0,
    int GithubIssuesCreated = 0,
    IReadOnlyList<string>? Errors = null,
    string? Message = null)
{
    public IReadOnlyList<string> Errors { get; init; } = Errors ?? Array.Empty<string>();
}

/// <summary>
/// Classifies the backlog items sitting under the synthetic
/// "Uncategorized" epic / feature buckets (created by
/// <c>BacklogGitHubImportService</c> when GitHub hierarchy inference
/// fails) into a proper epic → feature → story hierarchy using the
/// repository's configured LLM, with optional write-back of labels,
/// new issues, and native sub-issue links to GitHub.
/// </summary>
public interface IBacklogRecategorizeService
{
    /// <summary>
    /// Runs the classification for one repository.
    /// </summary>
    /// <returns>
    /// The populated <see cref="RecategorizeResult"/>, or <c>null</c>
    /// when the repository does not exist or the caller cannot see it
    /// (controller → 404, matching sync-github-issues).
    /// </returns>
    Task<RecategorizeResult?> RecategorizeAsync(
        Guid repositoryId,
        string userId,
        bool applyToGitHub,
        CancellationToken ct = default);
}
