// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Auth;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Andy.Issues.Api.Controllers;

[ApiController]
[Route("api/llm-settings")]
[Authorize]
public class LlmSettingsController : ControllerBase
{
    private readonly ILlmSettingService _service;

    public LlmSettingsController(ILlmSettingService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LlmSettingDto>>> List(CancellationToken ct)
    {
        var userId = GetUserId();
        var list = await _service.ListAsync(userId, ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LlmSettingDto>> Get(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var dto = await _service.GetAsync(id, userId, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<LlmSettingDto>> Create(
        [FromBody] CreateLlmSettingRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var (result, dto) = await _service.CreateAsync(request, userId, ct);
        return result switch
        {
            CreateLlmSettingResult.Created =>
                CreatedAtAction(nameof(Get), new { id = dto!.Id }, dto),
            CreateLlmSettingResult.InvalidProvider =>
                BadRequest(new { error = $"Unknown provider '{request.Provider}'. Use openai, anthropic, ollama, or custom." }),
            CreateLlmSettingResult.InvalidBaseUrl =>
                BadRequest(new { error = "BaseUrl must be an absolute http(s) URL when supplied." }),
            _ => StatusCode(500)
        };
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<LlmSettingDto>> Update(
        Guid id,
        [FromBody] UpdateLlmSettingRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var (result, dto) = await _service.UpdateAsync(id, request, userId, ct);
        return result switch
        {
            UpdateLlmSettingResult.Updated => Ok(dto),
            UpdateLlmSettingResult.NotFound => NotFound(),
            UpdateLlmSettingResult.InvalidProvider =>
                BadRequest(new { error = $"Unknown provider '{request.Provider}'. Use openai, anthropic, ollama, or custom." }),
            UpdateLlmSettingResult.InvalidBaseUrl =>
                BadRequest(new { error = "BaseUrl must be an absolute http(s) URL when supplied." }),
            _ => StatusCode(500)
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var ok = await _service.DeleteAsync(id, userId, ct);
        if (!ok) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:guid}/set-default")]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var ok = await _service.SetDefaultAsync(id, userId, ct);
        if (!ok) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Makes a trivial live call to the provider so the caller can
    /// verify their API key + model + base URL are reachable before
    /// relying on the setting from the "Suggest with AI" button.
    /// Returns <c>{ success, message }</c> so the UI can show a
    /// clear green/red banner.
    /// </summary>
    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<TestLlmSettingDto>> Test(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var outcome = await _service.TestAsync(id, userId, ct);
        return outcome.Outcome switch
        {
            TestLlmSettingOutcome.Ok =>
                Ok(new TestLlmSettingDto(true, outcome.Message ?? "Connection OK.")),
            TestLlmSettingOutcome.NotFound =>
                NotFound(),
            TestLlmSettingOutcome.ProviderRejected =>
                Ok(new TestLlmSettingDto(false, outcome.Message ?? "Provider rejected the request.")),
            _ => StatusCode(500)
        };
    }

    private string GetUserId() => User.RequireUserId();
}
