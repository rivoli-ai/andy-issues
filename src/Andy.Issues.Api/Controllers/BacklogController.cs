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
    private readonly IPullRequestStatusService _prStatus;
    private readonly IStoryRefinementService _refinement;

    public BacklogController(
        IBacklogService backlog,
        IBacklogAzureDevOpsSyncService azureSync,
        IBacklogAiService ai,
        IPullRequestStatusService prStatus,
        IStoryRefinementService refinement)
    {
        _backlog = backlog;
        _azureSync = azureSync;
        _ai = ai;
        _prStatus = prStatus;
        _refinement = refinement;
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

    // AH1 — resolve by GUID or `EPIC-42` short id. Routes without a
    // `:guid` constraint so both shapes hit the same endpoint; the
    // service parses and dispatches.
    [HttpGet("api/epics/{identifier}")]
    public async Task<ActionResult<EpicDto>> GetEpic(string identifier, CancellationToken ct)
    {
        var dto = await _backlog.GetEpicAsync(identifier, GetUserId(), ct);
        if (dto is null) return NotFound();
        return Ok(dto);
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

    // AH1 — resolve by GUID or `FEAT-7` short id.
    [HttpGet("api/features/{identifier}")]
    public async Task<ActionResult<FeatureDto>> GetFeature(string identifier, CancellationToken ct)
    {
        var dto = await _backlog.GetFeatureAsync(identifier, GetUserId(), ct);
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

    // AH1 — resolve by GUID or `STORY-13` short id.
    [HttpGet("api/stories/{identifier}")]
    public async Task<ActionResult<UserStoryDto>> GetStory(string identifier, CancellationToken ct)
    {
        var dto = await _backlog.GetStoryAsync(identifier, GetUserId(), ct);
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

    // #103 — late-join resync for the live progress UI. The
    // SignalR push from BacklogGenerationTracker.AdvanceAsync drives
    // the foreground update path; this endpoint covers the case
    // where the client missed an event (reconnect, page refresh
    // mid-run). Owner-scoped — generation rows aren't shared even
    // when the underlying repository is.
    [HttpGet("api/generations/{id:guid}")]
    [ProducesResponseType(typeof(BacklogGenerationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BacklogGenerationDto>> GetGeneration(
        Guid id,
        [FromServices] IBacklogGenerationTracker tracker,
        CancellationToken ct)
    {
        var dto = await tracker.GetAsync(id, GetUserId(), ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    // #101 — bulk delete across kinds. Reject empty bodies as 400 so
    // accidental no-ops surface immediately. Per-id failures are
    // collected on the response (200 OK with `failed[]`), not turned
    // into 4xx — matches the wire contract that ships partial
    // success.
    [HttpPost("api/backlog/bulk-delete")]
    [ProducesResponseType(typeof(BulkDeleteResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BulkDeleteResult>> BulkDelete(
        [FromBody] BulkDeleteRequest request,
        CancellationToken ct)
    {
        if (request.IsEmpty)
            return BadRequest(new { error = "At least one of epicIds/featureIds/storyIds must be non-empty." });

        var result = await _backlog.BulkDeleteAsync(request, GetUserId(), ct);
        return Ok(result);
    }

    // #88 — repo-wide PR status sync. Idempotent; safe to call on
    // every backlog load. Per-story failures don't abort the batch.
    [HttpPost("api/repositories/{repositoryId:guid}/sync-pr-statuses")]
    [ProducesResponseType(typeof(SyncPullRequestStatusesResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SyncPullRequestStatusesResultDto>> SyncPrStatuses(
        Guid repositoryId, CancellationToken ct)
    {
        var result = await _prStatus.SyncRepositoryAsync(repositoryId, GetUserId(), ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    // SP.0.4 (andy-issues#180 / conductor#1632) — kick off a story
    // refinement run. Long-running: returns 202 immediately with the
    // refineRunId; the client polls GET /api/stories/{id} or subscribes
    // to the `andy.issues.events.story.{id}.triaged` outbox topic to
    // observe completion. Idempotent under (storyId, agentId) for 5
    // minutes — see StoryRefinementService.IdempotencyWindow.
    [HttpPost("api/stories/{storyId:guid}/refine")]
    [ProducesResponseType(typeof(StoryRefineRunDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<StoryRefineRunDto>> RefineStory(
        Guid storyId,
        [FromBody] RefineStoryRequest? request,
        CancellationToken ct)
    {
        var result = await _refinement.RefineAsync(
            storyId, request ?? new RefineStoryRequest(), GetUserId(), ct);

        return result.Outcome switch
        {
            StoryRefineOutcome.Queued => Accepted(result.Run!),
            StoryRefineOutcome.NotFound => NotFound(),
            StoryRefineOutcome.Forbidden => Forbid(),
            StoryRefineOutcome.AgentUnavailable =>
                StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { error = result.Error ?? "Triage agent unavailable." }),
            _ => StatusCode(500)
        };
    }

    // #89 — single-story PR status check. Auto-transitions to Done
    // on a merged PR (status_updated=true) the same way the batch
    // endpoint does.
    [HttpGet("api/stories/{storyId:guid}/pr-status")]
    [ProducesResponseType(typeof(StoryPullRequestStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StoryPullRequestStatusDto>> GetStoryPrStatus(
        Guid storyId, CancellationToken ct)
    {
        var dto = await _prStatus.CheckStoryAsync(storyId, GetUserId(), ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    private string GetUserId() => User.RequireUserId();
}
