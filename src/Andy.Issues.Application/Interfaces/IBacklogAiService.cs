// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Application.Interfaces;

/// <summary>
/// AI-assisted content generation for backlog items. Today exposes a
/// single <c>SuggestContentAsync</c> verb powering the "Suggest with
/// AI" button on Conductor's backlog item editor. Kept focused — the
/// full draft-backlog generator is a separate interface
/// (<c>IDraftBacklogGenerator</c>) because its surface area is
/// different enough (code-index integration, whole-tree persistence)
/// to warrant splitting.
/// </summary>
public interface IBacklogAiService
{
    /// <summary>
    /// Generates a draft for the requested field of a backlog item
    /// (description or acceptanceCriteria). The caller's
    /// <see cref="SuggestContentRequest.CurrentContent"/> switches the
    /// prompt to refine-mode rather than generating from scratch.
    /// </summary>
    /// <returns>
    /// A tuple of <see cref="SuggestContentOutcome"/> and either the
    /// suggestion text (on success) or an error message (on any
    /// non-Ok outcome). Controllers map the outcome to HTTP status.
    /// </returns>
    Task<(SuggestContentOutcome Outcome, string? Suggestion, string? Error)> SuggestContentAsync(
        SuggestContentRequest request,
        string userId,
        CancellationToken ct = default);
}
