// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Security.Claims;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sandboxes")]
public class SandboxesController : ControllerBase
{
    private readonly ISandboxService _sandboxes;
    private readonly IPullRequestService _pullRequests;
    private readonly AppDbContext _db;

    public SandboxesController(
        ISandboxService sandboxes,
        IPullRequestService pullRequests,
        AppDbContext db)
    {
        _sandboxes = sandboxes;
        _pullRequests = pullRequests;
        _db = db;
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

    [HttpPost("{id:guid}/pull-request")]
    public async Task<ActionResult<object>> CreatePullRequest(
        Guid id,
        [FromBody] CreateSandboxPullRequestRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var sandbox = await _db.Sandboxes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sandbox is null) return NotFound();
        if (sandbox.OwnerUserId != userId) return Forbid();

        var repo = await _db.Repositories.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == sandbox.RepositoryId, ct);
        if (repo is null) return NotFound();

        var delegated = new CreatePullRequestFromSandboxRequest(
            SandboxId: sandbox.Id,
            Title: request.Title,
            Description: request.Description,
            SourceBranch: sandbox.Branch,
            TargetBranch: repo.DefaultBranch,
            StoryId: request.StoryId);

        var result = await _pullRequests.CreateFromSandboxAsync(sandbox.RepositoryId, delegated, userId, ct);
        return result.Outcome switch
        {
            PullRequestOutcome.Created => Ok(new { pullRequestUrl = result.PullRequestUrl }),
            PullRequestOutcome.NotFound => NotFound(),
            PullRequestOutcome.Forbidden => Forbid(),
            PullRequestOutcome.PushFailed => StatusCode(502, new { error = result.Error ?? "git push failed" }),
            PullRequestOutcome.ProviderFailed => StatusCode(502, new { error = result.Error ?? "provider rejected the pull request" }),
            PullRequestOutcome.UnsupportedProvider => BadRequest(new { error = result.Error }),
            _ => StatusCode(500)
        };
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
