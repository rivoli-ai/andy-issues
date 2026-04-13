// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;

namespace Andy.Issues.Application.Interfaces;

public enum DraftBacklogOutcome
{
    Generated = 0,
    RepositoryNotFound = 1,
    NotOwner = 2,
    NoLlmSetting = 3,
    CodeIndexNotReady = 4,
    LlmCallFailed = 5,
    ParseFailed = 6
}

public record DraftBacklogResult(
    DraftBacklogOutcome Outcome,
    BacklogDto? Backlog,
    string? Error);

/// <summary>
/// Generates a draft backlog (epics / features / stories) for a repository
/// by fetching its code summary from andy-code-index and sending it to the
/// repository's linked LLM. The generated items are persisted directly and
/// returned as a <see cref="BacklogDto"/>.
/// </summary>
public interface IDraftBacklogGenerator
{
    Task<DraftBacklogResult> GenerateAsync(
        Guid repositoryId,
        string userId,
        CancellationToken ct = default);
}
