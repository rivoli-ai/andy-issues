// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;

namespace Andy.Issues.Application.Interfaces;

public interface IBoardNotifier
{
    Task EpicAddedAsync(Guid repositoryId, EpicDto epic, CancellationToken ct = default);
    Task EpicUpdatedAsync(Guid repositoryId, EpicDto epic, CancellationToken ct = default);
    Task EpicDeletedAsync(Guid repositoryId, Guid epicId, CancellationToken ct = default);

    Task FeatureAddedAsync(Guid repositoryId, FeatureDto feature, CancellationToken ct = default);
    Task FeatureUpdatedAsync(Guid repositoryId, FeatureDto feature, CancellationToken ct = default);
    Task FeatureDeletedAsync(Guid repositoryId, Guid featureId, CancellationToken ct = default);

    Task StoryAddedAsync(Guid repositoryId, UserStoryDto story, CancellationToken ct = default);
    Task StoryUpdatedAsync(Guid repositoryId, UserStoryDto story, CancellationToken ct = default);
    Task StoryDeletedAsync(Guid repositoryId, Guid storyId, CancellationToken ct = default);

    // #103 — push a backlog-generation phase update to the repo
    // group so connected clients can drive a live progress UI.
    Task BacklogGenerationProgressAsync(
        Guid repositoryId,
        BacklogGenerationDto generation,
        CancellationToken ct = default);
}

public sealed class NullBoardNotifier : IBoardNotifier
{
    public Task EpicAddedAsync(Guid repositoryId, EpicDto epic, CancellationToken ct = default) => Task.CompletedTask;
    public Task EpicUpdatedAsync(Guid repositoryId, EpicDto epic, CancellationToken ct = default) => Task.CompletedTask;
    public Task EpicDeletedAsync(Guid repositoryId, Guid epicId, CancellationToken ct = default) => Task.CompletedTask;
    public Task FeatureAddedAsync(Guid repositoryId, FeatureDto feature, CancellationToken ct = default) => Task.CompletedTask;
    public Task FeatureUpdatedAsync(Guid repositoryId, FeatureDto feature, CancellationToken ct = default) => Task.CompletedTask;
    public Task FeatureDeletedAsync(Guid repositoryId, Guid featureId, CancellationToken ct = default) => Task.CompletedTask;
    public Task StoryAddedAsync(Guid repositoryId, UserStoryDto story, CancellationToken ct = default) => Task.CompletedTask;
    public Task StoryUpdatedAsync(Guid repositoryId, UserStoryDto story, CancellationToken ct = default) => Task.CompletedTask;
    public Task StoryDeletedAsync(Guid repositoryId, Guid storyId, CancellationToken ct = default) => Task.CompletedTask;
    public Task BacklogGenerationProgressAsync(Guid repositoryId, BacklogGenerationDto generation, CancellationToken ct = default) => Task.CompletedTask;
}
