// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Auth;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.Issues.Api.Controllers;

// Z1 — explicit verbs over the triage state machine. Free-form status
// PATCH is intentionally not exposed: every transition has a named
// endpoint so OpenAPI consumers (Z11) can see the lifecycle directly.
//
// Z11 — full [ProducesResponseType] coverage on every action so
// generated clients (and the schema contract test) see the exact set of
// status codes the controller emits. The conflict body for invalid
// transitions is `{ "error": string }` (the Conflict() shape returned
// from Transition).
[ApiController]
[Authorize]
[Route("api/triage")]
[Produces("application/json")]
public class TriageController : ControllerBase
{
    private readonly IIssueService _issues;

    public TriageController(IIssueService issues)
    {
        _issues = issues;
    }

    [HttpPost]
    [ProducesResponseType(typeof(IssueDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IssueDto>> Create(
        [FromBody] CreateIssueRequest request,
        CancellationToken ct)
    {
        var dto = await _issues.CreateAsync(request, GetUserId(), ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(IssueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IssueDto>> GetById(Guid id, CancellationToken ct)
    {
        var dto = await _issues.GetAsync(id, GetUserId(), ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    // Z9/Z10 — paginated list. `triageState` is case-insensitive; unknown
    // values fail soft (empty page) so CLI/MCP typos don't 4xx.
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<IssueDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<IssueDto>>> List(
        [FromQuery] string? triageState,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _issues.ListAsync(GetUserId(), triageState, page, pageSize, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/start")]
    [ProducesResponseType(typeof(IssueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(TriageConflictResponse), StatusCodes.Status409Conflict)]
    public Task<ActionResult<IssueDto>> Start(Guid id, CancellationToken ct) =>
        Transition(_issues.StartTriageAsync(id, GetUserId(), ct));

    [HttpPost("{id:guid}/complete")]
    [ProducesResponseType(typeof(IssueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(TriageConflictResponse), StatusCodes.Status409Conflict)]
    public Task<ActionResult<IssueDto>> Complete(Guid id, CancellationToken ct) =>
        Transition(_issues.CompleteTriageAsync(id, GetUserId(), ct));

    [HttpPost("{id:guid}/accept")]
    [ProducesResponseType(typeof(IssueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(TriageConflictResponse), StatusCodes.Status409Conflict)]
    public Task<ActionResult<IssueDto>> Accept(Guid id, CancellationToken ct) =>
        Transition(_issues.AcceptAsync(id, GetUserId(), ct));

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(typeof(IssueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(TriageConflictResponse), StatusCodes.Status409Conflict)]
    public Task<ActionResult<IssueDto>> Reject(Guid id, CancellationToken ct) =>
        Transition(_issues.RejectAsync(id, GetUserId(), ct));

    private async Task<ActionResult<IssueDto>> Transition(Task<IssueTriageResult> task)
    {
        var result = await task;
        return result.Outcome switch
        {
            IssueTriageOutcome.Updated => Ok(result.Issue),
            IssueTriageOutcome.NotFound => NotFound(),
            IssueTriageOutcome.InvalidTransition => Conflict(new TriageConflictResponse(result.Error ?? string.Empty)),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private string GetUserId() => User.RequireUserId();
}

// Body shape returned with HTTP 409 when a triage transition is rejected
// by the state machine. Named so generated OpenAPI clients see a
// concrete schema instead of an anonymous object.
public record TriageConflictResponse(string Error);
