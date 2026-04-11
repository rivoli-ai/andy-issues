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

    [HttpPost("{id:guid}/share")]
    public async Task<ActionResult<RepositoryShareDto>> Share(
        Guid id,
        [FromBody] ShareRepositoryRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var (result, dto) = await _repositoryService.ShareAsync(id, request.Email, userId, ct);
        return result switch
        {
            ShareResult.Created => Created($"/api/repositories/{id}/shares/{dto!.SharedWithUserId}", dto),
            ShareResult.AlreadyShared => Ok(dto),
            ShareResult.SelfShareRejected => BadRequest(new { error = "Cannot share a repository with yourself." }),
            ShareResult.EmailNotFound => NotFound(new { error = $"No user found for email '{request.Email}'." }),
            ShareResult.NotOwner => Forbid(),
            ShareResult.NotFound => NotFound(),
            _ => StatusCode(500)
        };
    }

    [HttpDelete("{id:guid}/share/{targetUserId}")]
    public async Task<IActionResult> Unshare(Guid id, string targetUserId, CancellationToken ct)
    {
        var userId = GetUserId();
        var ok = await _repositoryService.UnshareAsync(id, targetUserId, userId, ct);
        if (!ok) return NotFound();
        return NoContent();
    }

    [HttpGet("{id:guid}/shares")]
    public async Task<ActionResult<IReadOnlyList<RepositoryShareDto>>> ListShares(
        Guid id,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var shares = await _repositoryService.ListSharesAsync(id, userId, ct);
        if (shares is null) return NotFound();
        return Ok(shares);
    }

    [HttpPost("sync-github")]
    public async Task<ActionResult<SyncResult>> SyncGitHub(
        [FromBody] SyncGitHubRepositoriesRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _repositoryService.SyncFromGitHubAsync(userId, request.RepoIds, ct);
        if (result is null)
            return Unauthorized(new { error = "No GitHub access token linked for this user." });
        return Ok(result);
    }

    private string GetUserId()
    {
        return User.FindFirst("sub")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.Identity?.Name
            ?? "dev-user";
    }
}
