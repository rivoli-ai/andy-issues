// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Auth;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.Issues.Api.Controllers;

// #187 — unified list endpoint for Conductor cockpit consumers (AF2
// pipeline kanban + AF3 intake pane). The existing TriageController
// stays in place for the per-issue lifecycle verbs (create / start /
// complete / accept / reject / attach …) so this controller is a
// read-side overlay only.
//
// Why a separate controller (vs. extending /api/triage):
// - `/api/issues` is the canonical noun the cockpit consumers want;
//   `/api/triage` is the action surface. Routing on the noun keeps
//   OpenAPI clean for downstream code generators.
// - The legacy list endpoint at `GET /api/triage` returns a numeric-
//   paged <see cref="PagedResult{T}"/>. The unified endpoint returns
//   a cursor-paginated <see cref="IssueListResponse"/>. Different
//   contracts; co-locating them under one route prefix would force a
//   versioning fork.
[ApiController]
[Authorize]
[Route("api/issues")]
[Produces("application/json")]
public class IssuesController : ControllerBase
{
    private readonly IIssueService _issues;

    public IssuesController(IIssueService issues)
    {
        _issues = issues;
    }

    // GET /api/issues?state=<csv>&assignee=<id|none|me>&repository=<id>&limit=<n>&cursor=<opaque>
    //
    // - `state` accepts a comma-separated list; matches ANY-of. Allowed
    //   values mirror the wire form on the AF2/AF3 spec:
    //   `needs-triage`, `triaging`, `triaged`, `accepted`, `rejected`.
    //   The PascalCase enum names (`NeedsTriage`, …) are accepted as
    //   well for parity with the legacy MCP/CLI surface.
    // - `assignee=none` → unassigned only.
    //   `assignee=me`   → authenticated principal.
    //   `assignee=<id>` → specific user id.
    // - `repository` optionally scopes to one repository.
    // - `limit` defaults to 50, capped at 200.
    // - `cursor` is opaque; pass back the value from the previous
    //   response to fetch the next page.
    //
    // Validation contract: unknown `state` values 400 (unlike the
    // legacy `/api/triage` endpoint which fails soft). Conductor
    // declares one typed `listIssues(...)` protocol method for both
    // cockpit consumer panes — typo in the CSV should surface as an
    // error, not as a silently empty page.
    [HttpGet]
    [ProducesResponseType(typeof(IssueListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IssueListErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IssueListResponse>> List(
        [FromQuery] string? state,
        [FromQuery] string? assignee,
        [FromQuery] Guid? repository,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        CancellationToken ct)
    {
        var userId = User.RequireUserId();

        if (!IssueListQueryParser.TryParse(
                state, assignee, userId, repository, limit, cursor,
                out var query, out var error))
        {
            return BadRequest(new IssueListErrorResponse(error ?? "Invalid query."));
        }

        var response = await _issues.ListIssuesAsync(userId, query, ct);
        return Ok(response);
    }
}

// Body shape returned with HTTP 400 when the unified list endpoint
// rejects a query (unknown state, malformed assignee form, …). Named
// so generated OpenAPI clients see a concrete schema.
public record IssueListErrorResponse(string Error);
