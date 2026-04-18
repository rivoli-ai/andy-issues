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
[Authorize]
public class BacklogController : ControllerBase
{
    private readonly IBacklogService _backlog;
    private readonly IBacklogAzureDevOpsSyncService _azureSync;
    private readonly IBacklogAiService _ai;

    public BacklogController(
        IBacklogService backlog,
        IBacklogAzureDevOpsSyncService azureSync,
        IBacklogAiService ai)
    {
        _backlog = backlog;
        _azureSync = azureSync;
        _ai = ai;
    }

    /// <summary>
    /// Generates an AI draft for a backlog item's description or
    /// acceptance-criteria field. See issue #87 for the spec.
    /// </summary>
    [HttpPost("api/backlog/suggest")]
    public async Task<ActionResult<SuggestContentDto>> SuggestContent(
        [FromBody] SuggestContentRequest request,
        CancellationToken ct)
    {
        var (outcome, suggestion, error) = await _ai.SuggestContentAsync(
            request, GetUserId(), ct);

        return outcome switch
        {
            SuggestContentOutcome.Ok =>
                Ok(new SuggestContentDto(suggestion!)),
            SuggestContentOutcome.InvalidField =>
                BadRequest(new { error = error ?? "Invalid field." }),
            SuggestContentOutcome.InvalidItemType =>
                BadRequest(new { error = error ?? "Invalid itemType." }),
            SuggestContentOutcome.NoLlmSetting =>
                BadRequest(new { error = error ?? "No LLM setting configured." }),
            SuggestContentOutcome.RepositoryNotFound =>
                NotFound(),
            SuggestContentOutcome.NotOwner =>
                Forbid(),
            SuggestContentOutcome.LlmCallFailed =>
                StatusCode(502, new { error = error ?? "LLM call failed.", reason = "llmCallFailed" }),
            SuggestContentOutcome.ParseFailed =>
                StatusCode(502, new { error = error ?? "LLM returned unusable content.", reason = "parseFailed" }),
            _ => StatusCode(500)
        };
    }

    [HttpPost("api/repositories/{repositoryId:guid}/sync-azure-devops")]
    public async Task<ActionResult<SyncResult>> SyncAzureDevOps(Guid repositoryId, CancellationToken ct)
    {
        var push = await _azureSync.PushAsync(repositoryId, GetUserId(), ct);
        if (push is null) return NotFound();
        return Ok(push);
    }

    [HttpGet("api/repositories/{repositoryId:guid}/backlog")]
    public async Task<ActionResult<BacklogDto>> Get(Guid repositoryId, CancellationToken ct)
    {
        var dto = await _backlog.GetAsync(repositoryId, GetUserId(), ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpPost("api/repositories/{repositoryId:guid}/epics")]
    public async Task<ActionResult<EpicDto>> CreateEpic(
        Guid repositoryId,
        [FromBody] CreateEpicRequest request,
        CancellationToken ct)
    {
        var dto = await _backlog.AddEpicAsync(repositoryId, request, GetUserId(), ct);
        if (dto is null) return NotFound();
        return CreatedAtAction(nameof(Get), new { repositoryId }, dto);
    }

    [HttpPatch("api/epics/{epicId:guid}")]
    public async Task<ActionResult<EpicDto>> UpdateEpic(
        Guid epicId,
        [FromBody] UpdateEpicRequest request,
        CancellationToken ct)
    {
        var dto = await _backlog.UpdateEpicAsync(epicId, request, GetUserId(), ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpDelete("api/epics/{epicId:guid}")]
    public async Task<IActionResult> DeleteEpic(Guid epicId, CancellationToken ct)
    {
        var ok = await _backlog.DeleteEpicAsync(epicId, GetUserId(), ct);
        if (!ok) return NotFound();
        return NoContent();
    }

    [HttpPost("api/epics/{epicId:guid}/features")]
    public async Task<ActionResult<FeatureDto>> CreateFeature(
        Guid epicId,
        [FromBody] CreateFeatureRequest request,
        CancellationToken ct)
    {
        var dto = await _backlog.AddFeatureAsync(epicId, request, GetUserId(), ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpPatch("api/features/{featureId:guid}")]
    public async Task<ActionResult<FeatureDto>> UpdateFeature(
        Guid featureId,
        [FromBody] UpdateFeatureRequest request,
        CancellationToken ct)
    {
        var dto = await _backlog.UpdateFeatureAsync(featureId, request, GetUserId(), ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpDelete("api/features/{featureId:guid}")]
    public async Task<IActionResult> DeleteFeature(Guid featureId, CancellationToken ct)
    {
        var ok = await _backlog.DeleteFeatureAsync(featureId, GetUserId(), ct);
        if (!ok) return NotFound();
        return NoContent();
    }

    [HttpPost("api/features/{featureId:guid}/stories")]
    public async Task<ActionResult<UserStoryDto>> CreateStory(
        Guid featureId,
        [FromBody] CreateUserStoryRequest request,
        CancellationToken ct)
    {
        var dto = await _backlog.AddStoryAsync(featureId, request, GetUserId(), ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpPatch("api/stories/{storyId:guid}")]
    public async Task<ActionResult<UserStoryDto>> UpdateStory(
        Guid storyId,
        [FromBody] UpdateUserStoryRequest request,
        CancellationToken ct)
    {
        var dto = await _backlog.UpdateStoryAsync(storyId, request, GetUserId(), ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpPatch("api/stories/{storyId:guid}/status")]
    public async Task<ActionResult<UserStoryDto>> UpdateStoryStatus(
        Guid storyId,
        [FromBody] UpdateUserStoryStatusRequest request,
        CancellationToken ct)
    {
        var result = await _backlog.UpdateStoryStatusAsync(storyId, request, GetUserId(), ct);
        return result.Outcome switch
        {
            UserStoryStatusUpdateOutcome.Updated => Ok(result.Story),
            UserStoryStatusUpdateOutcome.NotFound => NotFound(),
            UserStoryStatusUpdateOutcome.InvalidStatus => BadRequest(new { error = result.Error }),
            UserStoryStatusUpdateOutcome.InvalidTransition => Conflict(new { error = result.Error }),
            _ => StatusCode(500)
        };
    }

    [HttpDelete("api/stories/{storyId:guid}")]
    public async Task<IActionResult> DeleteStory(Guid storyId, CancellationToken ct)
    {
        var ok = await _backlog.DeleteStoryAsync(storyId, GetUserId(), ct);
        if (!ok) return NotFound();
        return NoContent();
    }

    private string GetUserId() => User.RequireUserId();
}
