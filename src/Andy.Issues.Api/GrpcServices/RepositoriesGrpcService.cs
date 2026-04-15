// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Auth;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Api.Protos;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Andy.Issues.Api.GrpcServices;

[Authorize]
public class RepositoriesGrpcService : RepositoriesService.RepositoriesServiceBase
{
    private readonly IRepositoryService _service;

    public RepositoriesGrpcService(IRepositoryService service)
    {
        _service = service;
    }

    public override async Task<ListRepositoriesResponse> List(
        ListRepositoriesRequest request, ServerCallContext context)
    {
        if (!System.Enum.TryParse<RepositoryScope>(request.Scope, ignoreCase: true, out var scope))
            scope = RepositoryScope.Mine;

        var result = await _service.ListAsync(
            GetUserId(context), scope, request.Page, request.PageSize, context.CancellationToken);

        var response = new ListRepositoriesResponse
        {
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        };
        response.Items.AddRange(result.Items.Select(ToMessage));
        return response;
    }

    public override async Task<RepositoryResponse> Get(
        GetRepositoryRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid repository ID."));

        var dto = await _service.GetAsync(id, GetUserId(context), context.CancellationToken);
        if (dto is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Repository not found."));

        return new RepositoryResponse { Repository = ToMessage(dto) };
    }

    public override async Task<DeleteRepositoryResponse> Delete(
        DeleteRepositoryRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid repository ID."));

        var ok = await _service.DeleteAsync(id, GetUserId(context), context.CancellationToken);
        return new DeleteRepositoryResponse { Deleted = ok };
    }

    public override async Task<ShareRepositoryResponse> Share(
        ShareRepositoryRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.RepositoryId, out var repoId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid repository ID."));

        var (result, dto) = await _service.ShareAsync(
            repoId, request.Email, GetUserId(context), context.CancellationToken);

        var outcome = result switch
        {
            ShareResult.Created => "created",
            ShareResult.AlreadyShared => "already_shared",
            ShareResult.SelfShareRejected => "self_share",
            ShareResult.EmailNotFound => "email_not_found",
            ShareResult.NotOwner => "not_owner",
            ShareResult.NotFound => "not_found",
            _ => "error"
        };

        var response = new ShareRepositoryResponse { Outcome = outcome };
        if (dto is not null)
        {
            response.Share = new RepositoryShareMessage
            {
                Id = dto.Id.ToString(),
                RepositoryId = dto.RepositoryId.ToString(),
                SharedWithUserId = dto.SharedWithUserId,
                GrantedByUserId = dto.GrantedByUserId,
                GrantedAt = dto.GrantedAt.ToTimestamp()
            };
        }
        return response;
    }

    public override async Task<SyncResponse> SyncGitHub(
        SyncGitHubRequest request, ServerCallContext context)
    {
        var result = await _service.SyncFromGitHubAsync(
            GetUserId(context), request.RepoIds.ToList(), context.CancellationToken);

        if (result is null)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "No GitHub token linked."));

        return ToSyncResponse(result);
    }

    public override async Task<SyncResponse> SyncAzureDevOps(
        SyncAzureDevOpsRequest request, ServerCallContext context)
    {
        var result = await _service.SyncFromAzureDevOpsAsync(
            GetUserId(context), request.Organization, request.Project,
            request.RepoIds.ToList(), context.CancellationToken);

        if (result is null)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "No Azure DevOps token linked."));

        return ToSyncResponse(result);
    }

    private static RepositoryMessage ToMessage(Application.Dtos.RepositoryDto dto) => new()
    {
        Id = dto.Id.ToString(),
        OwnerUserId = dto.OwnerUserId,
        Name = dto.Name,
        Description = dto.Description,
        Provider = dto.Provider,
        CloneUrl = dto.CloneUrl,
        DefaultBranch = dto.DefaultBranch,
        ExternalId = dto.ExternalId,
        CodeIndexStatus = dto.CodeIndexStatus,
        CreatedAt = dto.CreatedAt.ToTimestamp(),
        UpdatedAt = dto.UpdatedAt?.ToTimestamp()
    };

    private static SyncResponse ToSyncResponse(SyncResult result)
    {
        var response = new SyncResponse
        {
            Added = result.Added,
            Updated = result.Updated,
            Skipped = result.Skipped
        };
        response.Errors.AddRange(result.Errors);
        return response;
    }

    private static string GetUserId(ServerCallContext context)
    {
        try
        {
            return context.GetHttpContext().User.RequireUserId();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, ex.Message));
        }
    }
}
