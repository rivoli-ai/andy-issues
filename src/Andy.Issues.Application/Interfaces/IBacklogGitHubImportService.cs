// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;

namespace Andy.Issues.Application.Interfaces;

/// <summary>
/// Imports a repository's GitHub issues into the local backlog. Issues
/// are classified by their <c>type:epic</c> / <c>type:feature</c> /
/// <c>type:story</c> label; hierarchy is inferred from markdown
/// task-list references (<c>- [ ] #123</c>) in parent issue bodies.
/// Items without a type label are skipped. Pull requests are ignored.
/// </summary>
public interface IBacklogGitHubImportService
{
    /// <summary>
    /// Imports or updates epics, features, and stories from GitHub for
    /// the given repository.
    /// </summary>
    /// <returns>
    /// A <see cref="SyncResult"/> with add/update/skip counts and any
    /// per-issue errors, or <c>null</c> if the user cannot see the
    /// repository.
    /// </returns>
    Task<SyncResult?> ImportAsync(
        Guid repositoryId,
        string userId,
        CancellationToken ct = default);
}
