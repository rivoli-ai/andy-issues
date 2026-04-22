// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Entities;

public class Feature
{
    public Guid Id { get; set; }

    /// <summary>
    /// Monotonic per-type sequence allocated by
    /// <c>IBacklogSequenceAllocator</c> before insert. Projected as
    /// <see cref="DisplayId"/>. Immutable once assigned. See AH1.
    /// </summary>
    public long Seq { get; internal set; }

    /// <summary>
    /// Human-readable short identifier (<c>FEAT-7</c>) derived from
    /// <see cref="Seq"/>. See AH1.
    /// </summary>
    public string DisplayId => $"FEAT-{Seq}";

    public Guid EpicId { get; set; }
    public Epic Epic { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Order { get; set; }
    public string? ExternalId { get; set; }

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

    public List<UserStory> Stories { get; set; } = new();
}
