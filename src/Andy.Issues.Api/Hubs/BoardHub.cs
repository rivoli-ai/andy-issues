// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Security.Claims;
using Andy.Issues.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Andy.Issues.Api.Hubs;

[Authorize]
public class BoardHub : Hub
{
    public const string Path = "/hubs/board";

    private readonly IRepositoryAccessGuard _guard;

    public BoardHub(IRepositoryAccessGuard guard)
    {
        _guard = guard;
    }

    public async Task JoinRepository(Guid repositoryId)
    {
        var userId = GetUserId();
        if (!await _guard.CanViewAsync(repositoryId, userId, Context.ConnectionAborted))
            throw new HubException($"Access denied to repository {repositoryId}.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(repositoryId), Context.ConnectionAborted);
    }

    public Task LeaveRepository(Guid repositoryId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(repositoryId), Context.ConnectionAborted);

    public static string GroupName(Guid repositoryId) => $"repo-{repositoryId}";

    private string GetUserId()
    {
        var user = Context.User;
        if (user is null) return "dev-user";
        return user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.Identity?.Name
            ?? "dev-user";
    }
}
