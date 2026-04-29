// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Auth;
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

    // Issue #65 — no silent "dev-user" fallback. The [Authorize]
    // attribute on this Hub guarantees Context.User is set with an
    // authenticated principal; if the claim chain still yields
    // nothing, fail loudly so the bad token / misconfiguration is
    // visible rather than silently attributed to a phantom user.
    private string GetUserId()
    {
        try
        {
            return (Context.User
                ?? throw new UnauthorizedAccessException(
                    "No authenticated user on the SignalR connection."))
                .RequireUserId();
        }
        catch (UnauthorizedAccessException ex)
        {
            // SignalR's Authorize attribute already rejects unauthenticated
            // connections, but if a token slips through with no `sub`,
            // surface it as a HubException so the client sees a clear
            // protocol-level error rather than a 500.
            throw new HubException(ex.Message);
        }
    }
}
