// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Security.Claims;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.Issues.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/artifact")]
public class ArtifactController : ControllerBase
{
    public const string AdminPermission = "artifact:admin";

    private readonly IArtifactFeedService _service;
    private readonly IPermissionChecker _permissions;

    public ArtifactController(IArtifactFeedService service, IPermissionChecker permissions)
    {
        _service = service;
        _permissions = permissions;
    }

    [HttpGet("enabled")]
    public async Task<ActionResult<IReadOnlyList<ArtifactFeedConfigDto>>> GetEnabled(CancellationToken ct)
    {
        var list = await _service.GetEnabledAsync(ct);
        return Ok(list);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ArtifactFeedConfigDto>>> List(CancellationToken ct)
    {
        if (!await IsAdminAsync(ct)) return Forbid();
        return Ok(await _service.ListAsync(ct));
    }

    [HttpGet("feeds")]
    public async Task<ActionResult<object>> BrowseFeeds(
        [FromQuery] string organization,
        CancellationToken ct)
    {
        if (!await IsAdminAsync(ct)) return Forbid();
        var result = await _service.BrowseAzureDevOpsFeedsAsync(GetUserId(), organization, ct);
        return result.Outcome switch
        {
            ArtifactFeedBrowseOutcome.Ok => Ok(new { feeds = result.Feeds }),
            ArtifactFeedBrowseOutcome.NoLinkedProvider => BadRequest(new { error = result.Error }),
            ArtifactFeedBrowseOutcome.ProviderError => StatusCode(502, new { error = result.Error }),
            _ => StatusCode(500)
        };
    }

    [HttpPost]
    public async Task<ActionResult<ArtifactFeedConfigDto>> Create(
        [FromBody] CreateArtifactFeedConfigRequest request,
        CancellationToken ct)
    {
        if (!await IsAdminAsync(ct)) return Forbid();
        var result = await _service.CreateAsync(request, ct);
        return MapResult(result, dto => CreatedAtAction(nameof(List), null, dto));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ArtifactFeedConfigDto>> Update(
        Guid id,
        [FromBody] UpdateArtifactFeedConfigRequest request,
        CancellationToken ct)
    {
        if (!await IsAdminAsync(ct)) return Forbid();
        var result = await _service.UpdateAsync(id, request, ct);
        return MapResult(result, dto => Ok(dto));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!await IsAdminAsync(ct)) return Forbid();
        var outcome = await _service.DeleteAsync(id, ct);
        return outcome switch
        {
            ArtifactFeedOutcome.Ok => NoContent(),
            ArtifactFeedOutcome.NotFound => NotFound(),
            _ => StatusCode(500)
        };
    }

    private ActionResult<ArtifactFeedConfigDto> MapResult(
        ArtifactFeedResult result,
        Func<ArtifactFeedConfigDto, ActionResult<ArtifactFeedConfigDto>> onOk) =>
        result.Outcome switch
        {
            ArtifactFeedOutcome.Ok => onOk(result.Dto!),
            ArtifactFeedOutcome.NotFound => NotFound(),
            ArtifactFeedOutcome.Invalid => BadRequest(new { error = result.Error }),
            ArtifactFeedOutcome.Conflict => Conflict(new { error = result.Error }),
            _ => StatusCode(500)
        };

    private Task<bool> IsAdminAsync(CancellationToken ct) =>
        _permissions.HasPermissionAsync(GetUserId(), AdminPermission, ct);

    private string GetUserId()
    {
        return User.FindFirst("sub")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.Identity?.Name
            ?? "dev-user";
    }
}
