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
public class UsersController : ControllerBase
{
    private readonly IUserDirectory _userDirectory;

    public UsersController(IUserDirectory userDirectory)
    {
        _userDirectory = userDirectory;
    }

    [HttpGet("suggest")]
    public async Task<ActionResult<IReadOnlyList<UserSuggestionDto>>> Suggest(
        [FromQuery] string q = "",
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(Array.Empty<UserSuggestionDto>());

        var callerId = GetUserId();
        var matches = await _userDirectory.SuggestAsync(q, callerId, limit, ct);
        var dtos = matches.Select(m => new UserSuggestionDto(m.UserId, m.Email, m.DisplayName)).ToList();
        return Ok(dtos);
    }

    private string GetUserId()
    {
        return User.FindFirst("sub")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.Identity?.Name
            ?? "dev-user";
    }
}
