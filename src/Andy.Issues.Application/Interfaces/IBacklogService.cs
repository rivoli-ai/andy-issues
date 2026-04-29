// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;

namespace Andy.Issues.Application.Interfaces;

public interface IBacklogService
{
    Task<BacklogDto?> GetAsync(Guid repositoryId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Resolves an epic by either GUID or display id (<c>EPIC-42</c>).
    /// Returns <c>null</c> when not found or when the caller lacks
    /// read access. See AH1.
    /// </summary>
    Task<EpicDto?> GetEpicAsync(string identifier, string userId, CancellationToken ct = default);

    /// <summary>
    /// Resolves a feature by either GUID or display id (<c>FEAT-7</c>).
    /// See AH1.
    /// </summary>
    Task<FeatureDto?> GetFeatureAsync(string identifier, string userId, CancellationToken ct = default);

    /// <summary>
    /// Resolves a story by either GUID or display id (<c>STORY-13</c>).
    /// See AH1.
    /// </summary>
    Task<UserStoryDto?> GetStoryAsync(string identifier, string userId, CancellationToken ct = default);

    Task<EpicDto?> AddEpicAsync(Guid repositoryId, CreateEpicRequest request, string userId, CancellationToken ct = default);
    Task<EpicDto?> UpdateEpicAsync(Guid epicId, UpdateEpicRequest request, string userId, CancellationToken ct = default);
    Task<bool> DeleteEpicAsync(Guid epicId, string userId, CancellationToken ct = default);

    Task<FeatureDto?> AddFeatureAsync(Guid epicId, CreateFeatureRequest request, string userId, CancellationToken ct = default);
    Task<FeatureDto?> UpdateFeatureAsync(Guid featureId, UpdateFeatureRequest request, string userId, CancellationToken ct = default);
    Task<bool> DeleteFeatureAsync(Guid featureId, string userId, CancellationToken ct = default);

    Task<UserStoryDto?> AddStoryAsync(Guid featureId, CreateUserStoryRequest request, string userId, CancellationToken ct = default);
    Task<UserStoryDto?> UpdateStoryAsync(Guid storyId, UpdateUserStoryRequest request, string userId, CancellationToken ct = default);
    Task<UserStoryStatusUpdateResult> UpdateStoryStatusAsync(Guid storyId, UpdateUserStoryStatusRequest request, string userId, CancellationToken ct = default);
    Task<bool> DeleteStoryAsync(Guid storyId, string userId, CancellationToken ct = default);

    // #101 — bulk delete across kinds. Per-item authorisation matches
    // single-delete semantics; partial success returns per-id failures
    // (NotFound, Forbidden). Cascading follows existing single-delete
    // (deleting an epic deletes its features + stories; deleting a
    // feature deletes its stories) — EF cascade rules at the DbContext
    // mapping handle this.
    Task<BulkDeleteResult> BulkDeleteAsync(BulkDeleteRequest request, string userId, CancellationToken ct = default);
}
