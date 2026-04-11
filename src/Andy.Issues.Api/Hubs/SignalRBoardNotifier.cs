// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace Andy.Issues.Api.Hubs;

public class SignalRBoardNotifier : IBoardNotifier
{
    private readonly IHubContext<BoardHub> _hub;

    public SignalRBoardNotifier(IHubContext<BoardHub> hub)
    {
        _hub = hub;
    }

    public Task EpicAddedAsync(Guid repositoryId, EpicDto epic, CancellationToken ct = default) =>
        Group(repositoryId).SendAsync("EpicAdded", epic, ct);

    public Task EpicUpdatedAsync(Guid repositoryId, EpicDto epic, CancellationToken ct = default) =>
        Group(repositoryId).SendAsync("EpicUpdated", epic, ct);

    public Task EpicDeletedAsync(Guid repositoryId, Guid epicId, CancellationToken ct = default) =>
        Group(repositoryId).SendAsync("EpicDeleted", epicId, ct);

    public Task FeatureAddedAsync(Guid repositoryId, FeatureDto feature, CancellationToken ct = default) =>
        Group(repositoryId).SendAsync("FeatureAdded", feature, ct);

    public Task FeatureUpdatedAsync(Guid repositoryId, FeatureDto feature, CancellationToken ct = default) =>
        Group(repositoryId).SendAsync("FeatureUpdated", feature, ct);

    public Task FeatureDeletedAsync(Guid repositoryId, Guid featureId, CancellationToken ct = default) =>
        Group(repositoryId).SendAsync("FeatureDeleted", featureId, ct);

    public Task StoryAddedAsync(Guid repositoryId, UserStoryDto story, CancellationToken ct = default) =>
        Group(repositoryId).SendAsync("StoryAdded", story, ct);

    public Task StoryUpdatedAsync(Guid repositoryId, UserStoryDto story, CancellationToken ct = default) =>
        Group(repositoryId).SendAsync("StoryUpdated", story, ct);

    public Task StoryDeletedAsync(Guid repositoryId, Guid storyId, CancellationToken ct = default) =>
        Group(repositoryId).SendAsync("StoryDeleted", storyId, ct);

    private IClientProxy Group(Guid repositoryId) =>
        _hub.Clients.Group(BoardHub.GroupName(repositoryId));
}
