// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Domain.Entities;

public class UserStory
{
    public Guid Id { get; set; }

    /// <summary>
    /// Monotonic per-type sequence allocated by
    /// <c>IBacklogSequenceAllocator</c> before insert. Projected as
    /// <see cref="DisplayId"/>. Immutable once assigned. See AH1.
    /// </summary>
    public long Seq { get; internal set; }

    /// <summary>
    /// Human-readable short identifier (<c>STORY-13</c>) derived from
    /// <see cref="Seq"/>. See AH1.
    /// </summary>
    public string DisplayId => $"STORY-{Seq}";

    public Guid FeatureId { get; set; }
    public Feature Feature { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public int? StoryPoints { get; set; }
    public UserStoryStatus Status { get; private set; } = UserStoryStatus.Draft;
    public string? PullRequestUrl { get; set; }
    public int Order { get; set; }
    public string? ExternalId { get; set; }
    public int? AzureDevOpsWorkItemId { get; set; }

    /// <summary>
    /// Labels carried from the external source. See Epic.Labels for the
    /// full rationale (conductor#670 Bug 2).
    /// </summary>
    public List<string> Labels { get; set; } = new();

    /// <summary>
    /// GitHub's typed Issue Type when present. See Epic.GitHubType.
    /// </summary>
    public string? GitHubType { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Stable sha256 (hex, lowercase) over the canonicalised
    /// title / description / labels / acceptance-criteria. Recomputed on
    /// every persist and emitted on every story.* outbox event so
    /// downstream services (andy-tasks, Conductor) can detect drift
    /// after a re-import. See andy-issues#181 / conductor#1627
    /// (Epic SP, story SP.7.1).
    ///
    /// Nullable so rows written before this column existed (and the
    /// SQLite EnsureCreated path) can be lazily backfilled on first read.
    /// </summary>
    public string? ContentHash { get; set; }

    public void SetStatus(UserStoryStatus next)
    {
        if (Status == UserStoryStatus.Done && next == UserStoryStatus.Draft)
            throw new InvalidOperationException(
                $"Invalid status transition: {Status} → {next}.");

        Status = next;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
