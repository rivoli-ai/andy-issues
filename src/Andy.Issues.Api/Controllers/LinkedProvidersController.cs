// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Auth;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.Issues.Api.Controllers;

[ApiController]
[Route("api/linked-providers")]
[Authorize]
public class LinkedProvidersController : ControllerBase
{
    private readonly ILinkedProviderService _service;

    public LinkedProvidersController(ILinkedProviderService service)
    {
        _service = service;
    }

    [HttpPost("pat")]
    public async Task<ActionResult<LinkedProviderDto>> LinkPat(
        [FromBody] LinkPatRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var (result, dto) = await _service.LinkPatAsync(request, userId, ct);
        return result switch
        {
            LinkPatResult.Linked => Ok(dto),
            LinkPatResult.InvalidProvider =>
                BadRequest(new { error = $"Unknown provider '{request.Provider}'. Use 'github' or 'azuredevops'." }),
            LinkPatResult.InvalidPat =>
                BadRequest(new { error = "PAT validation failed. The token may be invalid or expired." }),
            _ => StatusCode(500)
        };
    }

    [HttpPost]
    public async Task<ActionResult<LinkedProviderDto>> Upsert(
        [FromBody] CreateLinkedProviderRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var (result, dto) = await _service.UpsertAsync(request, userId, ct);
        return result switch
        {
            UpsertLinkedProviderResult.Created => Created($"/api/linked-providers", dto),
            UpsertLinkedProviderResult.Updated => Ok(dto),
            UpsertLinkedProviderResult.InvalidProvider =>
                BadRequest(new { error = $"Unknown provider '{request.Provider}'. Use 'github' or 'azuredevops'." }),
            _ => StatusCode(500)
        };
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LinkedProviderDto>>> List(CancellationToken ct)
    {
        var userId = GetUserId();
        var providers = await _service.ListAsync(userId, ct);
        return Ok(providers);
    }

    [HttpDelete("{provider}")]
    public async Task<IActionResult> Delete(string provider, CancellationToken ct)
    {
        var userId = GetUserId();
        var deleted = await _service.DeleteAsync(provider, userId, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    private string GetUserId() => User.RequireUserId();
}
