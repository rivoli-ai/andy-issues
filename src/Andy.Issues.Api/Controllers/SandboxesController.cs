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
[Route("api/sandboxes")]
public class SandboxesController : ControllerBase
{
    private readonly ISandboxService _sandboxes;

    public SandboxesController(ISandboxService sandboxes)
    {
        _sandboxes = sandboxes;
    }

    [HttpPost]
    public async Task<ActionResult<SandboxDto>> Create(
        [FromBody] CreateSandboxRequest request,
        CancellationToken ct)
    {
        var dto = await _sandboxes.CreateAsync(request, GetUserId(), ct);
        if (dto is null) return NotFound();
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SandboxDto>>> List(CancellationToken ct)
    {
        var list = await _sandboxes.ListAsync(GetUserId(), ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SandboxDto>> GetById(Guid id, CancellationToken ct)
    {
        var dto = await _sandboxes.GetAsync(id, GetUserId(), ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpGet("{id:guid}/connection")]
    public async Task<ActionResult<SandboxConnectionDto>> GetConnection(Guid id, CancellationToken ct)
    {
        var info = await _sandboxes.GetConnectionInfoAsync(id, GetUserId(), ct);
        if (info is null) return NotFound();
        return Ok(info);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ok = await _sandboxes.DestroyAsync(id, GetUserId(), ct);
        if (!ok) return NotFound();
        return NoContent();
    }

    private string GetUserId()
    {
        return User.FindFirst("sub")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.Identity?.Name
            ?? "dev-user";
    }
}
