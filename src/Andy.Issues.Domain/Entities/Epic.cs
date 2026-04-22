// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Entities;

public class Epic
{
    public Guid Id { get; set; }

    /// <summary>
    /// Monotonic per-type sequence allocated by
    /// <c>IBacklogSequenceAllocator</c> before insert. Projected as
    /// <see cref="DisplayId"/>. Immutable once assigned. See AH1.
    /// </summary>
    public long Seq { get; internal set; }

    /// <summary>
    /// Human-readable short identifier (<c>EPIC-42</c>) derived from
    /// <see cref="Seq"/>. Safe to display, copy, and cross-reference
    /// from chat, commits, PRs. See AH1.
    /// </summary>
    public string DisplayId => $"EPIC-{Seq}";

    public Guid RepositoryId { get; set; }
    public Repository Repository { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Order { get; set; }
    public string? ExternalId { get; set; }

    /// <summary>
    /// Labels carried from the external source (e.g. GitHub). Populated
    /// by <c>BacklogGitHubImportService</c> on every sync so later syncs
    /// can detect classification changes (Epic ↔ Feature ↔ Story) and
    /// re-home the row in the correct entity table.
    /// See conductor#670 Bug 2.
    /// </summary>
    public List<string> Labels { get; set; } = new();

    /// <summary>
    /// GitHub's typed Issue Type (<c>Bug</c> / <c>Feature</c> / <c>Task</c>
    /// from the new typed Issue Types feature), or <c>null</c> when the
    /// source doesn't expose one. Stored so the Conductor UI can show
    /// the type badge without refetching and so re-sync can detect type
    /// flips even when labels are unchanged.
    /// </summary>
    public string? GitHubType { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public List<Feature> Features { get; set; } = new();
}
