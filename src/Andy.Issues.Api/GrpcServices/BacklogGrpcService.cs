// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Security.Claims;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Api.Protos;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Andy.Issues.Api.GrpcServices;

[Authorize]
public class BacklogGrpcService : Protos.BacklogService.BacklogServiceBase
{
    private readonly IBacklogService _service;

    public BacklogGrpcService(IBacklogService service)
    {
        _service = service;
    }

    public override async Task<BacklogResponse> GetBacklog(
        GetBacklogRequest request, ServerCallContext context)
    {
        var repoId = ParseGuid(request.RepositoryId);
        var dto = await _service.GetAsync(repoId, GetUserId(context), context.CancellationToken);
        if (dto is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Backlog not found."));

        var response = new BacklogResponse { RepositoryId = dto.RepositoryId.ToString() };
        response.Epics.AddRange(dto.Epics.Select(ToEpicMessage));
        return response;
    }

    public override async Task<EpicMessage> AddEpic(
        AddEpicRequest request, ServerCallContext context)
    {
        var repoId = ParseGuid(request.RepositoryId);
        var dto = await _service.AddEpicAsync(repoId,
            new CreateEpicRequest(request.Title, request.Description, request.Order, request.ExternalId),
            GetUserId(context), context.CancellationToken);
        if (dto is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Repository not found."));
        return ToEpicMessage(dto);
    }

    public override async Task<EpicMessage> UpdateEpic(
        Protos.UpdateEpicRequest request, ServerCallContext context)
    {
        var epicId = ParseGuid(request.EpicId);
        var dto = await _service.UpdateEpicAsync(epicId,
            new Application.Requests.UpdateEpicRequest(request.Title, request.Description, request.Order),
            GetUserId(context), context.CancellationToken);
        if (dto is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Epic not found."));
        return ToEpicMessage(dto);
    }

    public override async Task<DeleteResponse> DeleteEpic(
        DeleteEpicRequest request, ServerCallContext context)
    {
        var epicId = ParseGuid(request.EpicId);
        var ok = await _service.DeleteEpicAsync(epicId, GetUserId(context), context.CancellationToken);
        return new DeleteResponse { Deleted = ok };
    }

    public override async Task<FeatureMessage> AddFeature(
        AddFeatureRequest request, ServerCallContext context)
    {
        var epicId = ParseGuid(request.EpicId);
        var dto = await _service.AddFeatureAsync(epicId,
            new CreateFeatureRequest(request.Title, request.Description, request.Order, request.ExternalId),
            GetUserId(context), context.CancellationToken);
        if (dto is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Epic not found."));
        return ToFeatureMessage(dto);
    }

    public override async Task<FeatureMessage> UpdateFeature(
        Protos.UpdateFeatureRequest request, ServerCallContext context)
    {
        var featureId = ParseGuid(request.FeatureId);
        var dto = await _service.UpdateFeatureAsync(featureId,
            new Application.Requests.UpdateFeatureRequest(request.Title, request.Description, request.Order),
            GetUserId(context), context.CancellationToken);
        if (dto is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Feature not found."));
        return ToFeatureMessage(dto);
    }

    public override async Task<DeleteResponse> DeleteFeature(
        DeleteFeatureRequest request, ServerCallContext context)
    {
        var featureId = ParseGuid(request.FeatureId);
        var ok = await _service.DeleteFeatureAsync(featureId, GetUserId(context), context.CancellationToken);
        return new DeleteResponse { Deleted = ok };
    }

    public override async Task<UserStoryMessage> AddStory(
        AddStoryRequest request, ServerCallContext context)
    {
        var featureId = ParseGuid(request.FeatureId);
        var dto = await _service.AddStoryAsync(featureId,
            new CreateUserStoryRequest(
                request.Title, request.Description, request.AcceptanceCriteria,
                request.StoryPoints, request.Order, request.ExternalId),
            GetUserId(context), context.CancellationToken);
        if (dto is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Feature not found."));
        return ToStoryMessage(dto);
    }

    public override async Task<UserStoryMessage> UpdateStory(
        Protos.UpdateStoryRequest request, ServerCallContext context)
    {
        var storyId = ParseGuid(request.StoryId);
        var dto = await _service.UpdateStoryAsync(storyId,
            new UpdateUserStoryRequest(
                request.Title, request.Description, request.AcceptanceCriteria,
                request.StoryPoints, request.Order),
            GetUserId(context), context.CancellationToken);
        if (dto is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Story not found."));
        return ToStoryMessage(dto);
    }

    public override async Task<UpdateStoryStatusResponse> UpdateStoryStatus(
        Protos.UpdateStoryStatusRequest request, ServerCallContext context)
    {
        var storyId = ParseGuid(request.StoryId);
        var result = await _service.UpdateStoryStatusAsync(storyId,
            new UpdateUserStoryStatusRequest(request.Status, request.PullRequestUrl),
            GetUserId(context), context.CancellationToken);

        var outcome = result.Outcome switch
        {
            UserStoryStatusUpdateOutcome.Updated => "updated",
            UserStoryStatusUpdateOutcome.NotFound => "not_found",
            UserStoryStatusUpdateOutcome.InvalidStatus => "invalid_status",
            UserStoryStatusUpdateOutcome.InvalidTransition => "invalid_transition",
            _ => "error"
        };

        var response = new UpdateStoryStatusResponse { Outcome = outcome, Error = result.Error };
        if (result.Story is not null)
            response.Story = ToStoryMessage(result.Story);
        return response;
    }

    public override async Task<DeleteResponse> DeleteStory(
        DeleteStoryRequest request, ServerCallContext context)
    {
        var storyId = ParseGuid(request.StoryId);
        var ok = await _service.DeleteStoryAsync(storyId, GetUserId(context), context.CancellationToken);
        return new DeleteResponse { Deleted = ok };
    }

    private static EpicMessage ToEpicMessage(EpicDto dto)
    {
        var msg = new EpicMessage
        {
            Id = dto.Id.ToString(),
            RepositoryId = dto.RepositoryId.ToString(),
            Title = dto.Title,
            Description = dto.Description,
            Order = dto.Order,
            ExternalId = dto.ExternalId,
            CreatedAt = dto.CreatedAt.ToTimestamp(),
            UpdatedAt = dto.UpdatedAt?.ToTimestamp()
        };
        msg.Features.AddRange(dto.Features.Select(ToFeatureMessage));
        return msg;
    }

    private static FeatureMessage ToFeatureMessage(FeatureDto dto)
    {
        var msg = new FeatureMessage
        {
            Id = dto.Id.ToString(),
            EpicId = dto.EpicId.ToString(),
            Title = dto.Title,
            Description = dto.Description,
            Order = dto.Order,
            ExternalId = dto.ExternalId,
            CreatedAt = dto.CreatedAt.ToTimestamp(),
            UpdatedAt = dto.UpdatedAt?.ToTimestamp()
        };
        msg.Stories.AddRange(dto.Stories.Select(ToStoryMessage));
        return msg;
    }

    private static UserStoryMessage ToStoryMessage(UserStoryDto dto) => new()
    {
        Id = dto.Id.ToString(),
        FeatureId = dto.FeatureId.ToString(),
        Title = dto.Title,
        Description = dto.Description,
        AcceptanceCriteria = dto.AcceptanceCriteria,
        StoryPoints = dto.StoryPoints,
        Status = dto.Status,
        PullRequestUrl = dto.PullRequestUrl,
        Order = dto.Order,
        ExternalId = dto.ExternalId,
        CreatedAt = dto.CreatedAt.ToTimestamp(),
        UpdatedAt = dto.UpdatedAt?.ToTimestamp()
    };

    private static Guid ParseGuid(string value)
    {
        if (!Guid.TryParse(value, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid GUID: '{value}'."));
        return id;
    }

    private static string GetUserId(ServerCallContext context)
    {
        var user = context.GetHttpContext().User;
        return user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.Identity?.Name
            ?? "dev-user";
    }
}
