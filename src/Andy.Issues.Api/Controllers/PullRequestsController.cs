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
public class PullRequestsController : ControllerBase
{
    private readonly IPullRequestStatusService _service;

    public PullRequestsController(IPullRequestStatusService service)
    {
        _service = service;
    }

    // #90 — resolve a PR URL to its head branch. Sandbox creation
    // (Conductor #521) needs this so it can check out the right branch
    // for the work in progress on a PR. 400 for an unparseable URL,
    // 404 when the PR doesn't exist or the caller has no
    // LinkedProvider credentials to query it.
    [HttpGet("api/pr-head-branch")]
    [ProducesResponseType(typeof(PullRequestHeadBranchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PullRequestHeadBranchDto>> GetHeadBranch(
        [FromQuery] string url,
        CancellationToken ct)
    {
        var (outcome, branch) = await _service.GetHeadBranchByUrlAsync(
            url, GetUserId(), ct);

        return outcome switch
        {
            HeadBranchOutcome.Ok => Ok(new PullRequestHeadBranchDto(branch!)),
            HeadBranchOutcome.BadUrl => BadRequest(new
            {
                error = "URL is not a recognised GitHub or Azure DevOps pull request URL."
            }),
            HeadBranchOutcome.NotFound => NotFound(new
            {
                error = "Pull request not found, or no linked provider for the caller."
            }),
            _ => StatusCode(500)
        };
    }

    private string GetUserId() => User.RequireUserId();
}
