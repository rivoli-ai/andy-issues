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

// #103 — `GenerationId` references the BacklogGeneration row that
// the IBacklogGenerationTracker created at the start of this run.
// Null only when the run failed before the row was created (e.g.,
// repository not found / not owner).
public record DraftBacklogResult(
    DraftBacklogOutcome Outcome,
    BacklogDto? Backlog,
    string? Error,
    Guid? GenerationId = null);

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
