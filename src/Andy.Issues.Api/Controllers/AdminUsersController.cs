// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Auth;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.Issues.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/users")]
public sealed class AdminUsersController(IAndyRbacUsersClient users, IAndySettingsClient settings) : ControllerBase
{
    [HttpGet("access")]
    public async Task<IActionResult> Access(CancellationToken ct)
    {
        var canRead = AdminUsersAuthorization.CanRead(User);
        var configured = canRead ? await settings.GetAsync<string>("andy-rbac:admin-url", ct) : null;
        var manageUrl = Uri.TryCreate(configured, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri : null;
        return Ok(new { canRead, manageUrl });
    }

    [HttpGet]
    [Authorize(Policy = AdminUsersAuthorization.Policy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AdminUsersDto>> List(string? query, string? role, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        try { return Ok(await users.ListAsync(User.RequireUserId(), query, role, skip, take, ct)); }
        catch (HttpRequestException) { return StatusCode(502, new { error = "Andy RBAC user listing is unavailable. Please retry." }); }
    }
}
