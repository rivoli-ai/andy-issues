// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;

namespace Andy.Issues.Application.Interfaces;

public interface IBacklogService
{
    Task<BacklogDto?> GetAsync(Guid repositoryId, string userId, CancellationToken ct = default);

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
}
