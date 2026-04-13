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
public class SandboxesGrpcService : SandboxesService.SandboxesServiceBase
{
    private readonly ISandboxService _service;

    public SandboxesGrpcService(ISandboxService service)
    {
        _service = service;
    }

    public override async Task<SandboxMessage> Create(
        CreateSandboxGrpcRequest request, ServerCallContext context)
    {
        var repoId = ParseGuid(request.RepositoryId);
        var dto = await _service.CreateAsync(
            new CreateSandboxRequest(repoId, request.Branch, null),
            GetUserId(context), context.CancellationToken);

        if (dto is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Repository not found."));

        return ToMessage(dto);
    }

    public override async Task<ListSandboxesResponse> List(
        ListSandboxesRequest request, ServerCallContext context)
    {
        var list = await _service.ListAsync(GetUserId(context), context.CancellationToken);
        var response = new ListSandboxesResponse();
        response.Sandboxes.AddRange(list.Select(ToMessage));
        return response;
    }

    public override async Task<SandboxMessage> Get(
        GetSandboxRequest request, ServerCallContext context)
    {
        var id = ParseGuid(request.Id);
        var dto = await _service.GetAsync(id, GetUserId(context), context.CancellationToken);
        if (dto is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Sandbox not found."));
        return ToMessage(dto);
    }

    public override async Task<DestroySandboxResponse> Destroy(
        DestroySandboxRequest request, ServerCallContext context)
    {
        var id = ParseGuid(request.Id);
        var ok = await _service.DestroyAsync(id, GetUserId(context), context.CancellationToken);
        return new DestroySandboxResponse { Destroyed = ok };
    }

    public override async Task<SandboxConnectionMessage> GetConnectionInfo(
        GetSandboxConnectionRequest request, ServerCallContext context)
    {
        var id = ParseGuid(request.Id);
        var info = await _service.GetConnectionInfoAsync(id, GetUserId(context), context.CancellationToken);
        if (info is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Sandbox not found or no connection info."));

        return new SandboxConnectionMessage
        {
            IdeEndpoint = info.IdeEndpoint,
            SshEndpoint = info.SshEndpoint,
            VncEndpoint = info.VncEndpoint
        };
    }

    private static SandboxMessage ToMessage(SandboxDto dto) => new()
    {
        Id = dto.Id.ToString(),
        RepositoryId = dto.RepositoryId.ToString(),
        OwnerUserId = dto.OwnerUserId,
        Branch = dto.Branch,
        Status = dto.Status,
        IdeEndpoint = dto.IdeEndpoint,
        VncEndpoint = dto.VncEndpoint,
        ContainerId = dto.ContainerId,
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
