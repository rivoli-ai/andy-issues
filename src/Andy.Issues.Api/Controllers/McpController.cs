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
[Route("api/mcp")]
public class McpController : ControllerBase
{
    public const string AdminPermission = "mcp:admin";

    private readonly IMcpConfigService _service;
    private readonly IPermissionChecker _permissions;

    public McpController(IMcpConfigService service, IPermissionChecker permissions)
    {
        _service = service;
        _permissions = permissions;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<McpServerConfigDto>>> List(CancellationToken ct)
    {
        var list = await _service.ListForUserAsync(GetUserId(), ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<McpServerConfigDto>> Get(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var isAdmin = await IsAdminAsync(userId, ct);
        var dto = await _service.GetAsync(id, userId, isAdmin, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<McpServerConfigDto>> Create(
        [FromBody] CreateMcpServerConfigRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var isAdmin = await IsAdminAsync(userId, ct);
        var result = await _service.CreateAsync(request, userId, isAdmin, ct);
        return MapResult(result, dto => CreatedAtAction(nameof(Get), new { id = dto.Id }, dto));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<McpServerConfigDto>> Update(
        Guid id,
        [FromBody] UpdateMcpServerConfigRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var isAdmin = await IsAdminAsync(userId, ct);
        var result = await _service.UpdateAsync(id, request, userId, isAdmin, ct);
        return MapResult(result, dto => Ok(dto));
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<ActionResult<McpServerConfigDto>> Toggle(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var isAdmin = await IsAdminAsync(userId, ct);
        var result = await _service.ToggleAsync(id, userId, isAdmin, ct);
        return MapResult(result, dto => Ok(dto));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var isAdmin = await IsAdminAsync(userId, ct);
        var outcome = await _service.DeleteAsync(id, userId, isAdmin, ct);
        return outcome switch
        {
            McpConfigOutcome.Ok => NoContent(),
            McpConfigOutcome.NotFound => NotFound(),
            McpConfigOutcome.Forbidden => Forbid(),
            _ => StatusCode(500)
        };
    }

    private ActionResult<McpServerConfigDto> MapResult(
        McpConfigResult result,
        Func<McpServerConfigDto, ActionResult<McpServerConfigDto>> onOk) =>
        result.Outcome switch
        {
            McpConfigOutcome.Ok => onOk(result.Dto!),
            McpConfigOutcome.NotFound => NotFound(),
            McpConfigOutcome.Forbidden => Forbid(),
            McpConfigOutcome.Invalid => BadRequest(new { error = result.Error }),
            McpConfigOutcome.Conflict => Conflict(new { error = result.Error }),
            _ => StatusCode(500)
        };

    private Task<bool> IsAdminAsync(string userId, CancellationToken ct) =>
        _permissions.HasPermissionAsync(userId, AdminPermission, ct);

    private string GetUserId()
    {
        return User.FindFirst("sub")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.Identity?.Name
            ?? "dev-user";
    }
}
