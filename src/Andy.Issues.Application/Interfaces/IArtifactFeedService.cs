// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;

namespace Andy.Issues.Application.Interfaces;

public enum ArtifactFeedOutcome
{
    Ok = 0,
    NotFound = 1,
    Invalid = 2,
    Conflict = 3
}

public record ArtifactFeedResult(
    ArtifactFeedOutcome Outcome,
    ArtifactFeedConfigDto? Dto,
    string? Error);

public enum ArtifactFeedBrowseOutcome
{
    Ok = 0,
    NoLinkedProvider = 1,
    ProviderError = 2
}

public record ArtifactFeedBrowseResult(
    ArtifactFeedBrowseOutcome Outcome,
    IReadOnlyList<AzureDevOpsFeedInfo>? Feeds,
    string? Error);

public interface IArtifactFeedService
{
    Task<IReadOnlyList<ArtifactFeedConfigDto>> GetEnabledAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ArtifactFeedConfigDto>> ListAsync(CancellationToken ct = default);
    Task<ArtifactFeedConfigDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ArtifactFeedResult> CreateAsync(CreateArtifactFeedConfigRequest request, CancellationToken ct = default);
    Task<ArtifactFeedResult> UpdateAsync(Guid id, UpdateArtifactFeedConfigRequest request, CancellationToken ct = default);
    Task<ArtifactFeedOutcome> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Live-browse Azure DevOps feeds available to the caller's linked Azure DevOps PAT
    /// for the given organization. Used by admin screens to pick a feed to register.
    /// </summary>
    Task<ArtifactFeedBrowseResult> BrowseAzureDevOpsFeedsAsync(
        string userId,
        string organization,
        CancellationToken ct = default);
}
