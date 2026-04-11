// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Security.Claims;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.Issues.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RepositoriesController : ControllerBase
{
    private readonly IRepositoryService _repositoryService;

    public RepositoriesController(IRepositoryService repositoryService)
    {
        _repositoryService = repositoryService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<RepositoryDto>>> List(
        [FromQuery] string scope = "mine",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<RepositoryScope>(scope, ignoreCase: true, out var parsed))
            return BadRequest(new { error = $"Unknown scope '{scope}'. Use mine|shared|all." });

        var userId = GetUserId();
        var result = await _repositoryService.ListAsync(userId, parsed, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RepositoryDto>> Get(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var repo = await _repositoryService.GetAsync(id, userId, ct);
        if (repo is null) return NotFound();
        return Ok(repo);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var deleted = await _repositoryService.DeleteAsync(id, userId, ct);
        if (!deleted) return NotFound();
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
