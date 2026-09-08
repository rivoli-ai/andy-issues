// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Auth;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Andy.Issues.Api.Controllers;

[ApiController]
[Route("api/ai-config")]
[Authorize(Policy = "AiConfigOwner")]
[EnableRateLimiting("AiConfig")]
public sealed class AiConfigController(IAiConfigService service) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("Sensitive: owner-only AI configuration for sandbox injection")]
    [EndpointDescription("Returns a RAW API KEY. Bearer authentication and ownership are required, including ownership of repository overrides. Limited to 10 requests/minute per user per service instance. Access is audited and responses use Cache-Control: no-store. Use TLS at the remote edge; keep keys in memory only, never logs or persistent consumer caches.")]
    [ProducesResponseType<AiConfigDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AiConfigDto>> Get([FromQuery] Guid? repositoryId, CancellationToken ct)
    {
        var result = await service.GetAsync(User.RequireUserId(), repositoryId, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
