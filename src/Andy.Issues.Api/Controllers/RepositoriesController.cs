// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Auth;
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
    private readonly IPullRequestService _pullRequestService;
    private readonly IDraftBacklogGenerator _draftBacklogGenerator;
    private readonly IBacklogGitHubImportService _backlogGitHubImportService;

    public RepositoriesController(
        IRepositoryService repositoryService,
        IPullRequestService pullRequestService,
        IDraftBacklogGenerator draftBacklogGenerator,
        IBacklogGitHubImportService backlogGitHubImportService)
    {
        _repositoryService = repositoryService;
        _pullRequestService = pullRequestService;
        _draftBacklogGenerator = draftBacklogGenerator;
        _backlogGitHubImportService = backlogGitHubImportService;
    }

    [HttpPost("{id:guid}/generate-backlog")]
    public async Task<ActionResult<BacklogDto>> GenerateBacklog(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _draftBacklogGenerator.GenerateAsync(id, userId, ct);
        return result.Outcome switch
        {
            DraftBacklogOutcome.Generated => Ok(result.Backlog),
            DraftBacklogOutcome.RepositoryNotFound => NotFound(),
            DraftBacklogOutcome.NotOwner => Forbid(),
            DraftBacklogOutcome.NoLlmSetting => BadRequest(new { error = result.Error }),
            DraftBacklogOutcome.CodeIndexNotReady => StatusCode(
                502, new { error = result.Error, reason = "codeIndexNotReady" }),
            DraftBacklogOutcome.LlmCallFailed => StatusCode(
                502, new { error = result.Error, reason = "llmCallFailed" }),
            DraftBacklogOutcome.ParseFailed => StatusCode(
                502, new { error = result.Error, reason = "parseFailed" }),
            _ => StatusCode(500)
        };
    }

    [HttpPost("{id:guid}/pull-request")]
    public async Task<ActionResult<object>> CreatePullRequest(
        Guid id,
        [FromBody] CreatePullRequestFromSandboxRequest request,
        CancellationToken ct)
    {
        var result = await _pullRequestService.CreateFromSandboxAsync(id, request, GetUserId(), ct);
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

    [HttpPost]
    public async Task<ActionResult<RepositoryDto>> Create(
        [FromBody] CreateRepositoryRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var (result, dto) = await _repositoryService.CreateAsync(request, userId, ct);
        return result switch
        {
            CreateRepositoryResult.Created =>
                CreatedAtAction(nameof(Get), new { id = dto!.Id }, dto),
            CreateRepositoryResult.AlreadyExists =>
                // Idempotent: return 200 with the existing dto so the
                // Conductor "Add repository" sheet can treat repeat
                // submissions as a no-op without surfacing an error.
                Ok(dto),
            CreateRepositoryResult.InvalidProvider =>
                BadRequest(new { error = $"Unknown provider '{request.Provider}'. Use 'github' or 'azuredevops'." }),
            CreateRepositoryResult.InvalidCloneUrl =>
                BadRequest(new { error = "CloneUrl is required and must be an absolute http(s) URL." }),
            _ => StatusCode(500)
        };
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

    /// <summary>
    /// Imports the repository's GitHub issues into the local backlog.
    /// Classifies by <c>type:epic</c> / <c>type:feature</c> /
    /// <c>type:story</c> labels, upserts by issue number, and infers
    /// hierarchy from markdown task-list references in parent bodies.
    /// </summary>
    [HttpPost("{id:guid}/sync-github-issues")]
    public async Task<ActionResult<SyncResult>> SyncGitHubIssues(
        Guid id,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _backlogGitHubImportService.ImportAsync(id, userId, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPatch("{id:guid}/llm-setting")]
    public async Task<IActionResult> SetLlmSetting(
        Guid id,
        [FromBody] UpdateRepositoryLlmRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _repositoryService.SetLlmSettingAsync(id, request.LlmSettingId, userId, ct);
        return result switch
        {
            SetLlmResult.Updated => NoContent(),
            SetLlmResult.RepositoryNotFound => NotFound(),
            SetLlmResult.LlmSettingNotFound => NotFound(new { error = "LLM setting not found." }),
            SetLlmResult.NotOwner => Forbid(),
            _ => StatusCode(500)
        };
    }

    [HttpPatch("{id:guid}/azure-identity")]
    public async Task<IActionResult> SetAzureIdentity(
        Guid id,
        [FromBody] UpdateRepositoryAzureIdentityRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _repositoryService.SetAzureIdentityAsync(
            id,
            request.ClientId,
            request.ClientSecret,
            request.TenantId,
            request.SubscriptionId,
            userId,
            ct);
        return result switch
        {
            SetAzureIdentityResult.Updated => NoContent(),
            SetAzureIdentityResult.NotFound => NotFound(),
            SetAzureIdentityResult.NotOwner => Forbid(),
            _ => StatusCode(500)
        };
    }

    [HttpPost("{id:guid}/verify-azure-identity")]
    public async Task<ActionResult<VerifyAzureIdentityResult>> VerifyAzureIdentity(
        Guid id,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _repositoryService.VerifyAzureIdentityAsync(id, userId, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPost("sync-azure")]
    public async Task<ActionResult<SyncResult>> SyncAzureDevOps(
        [FromBody] SyncAzureDevOpsRepositoriesRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _repositoryService.SyncFromAzureDevOpsAsync(
            userId, request.Organization, request.Project, request.RepoIds, ct);
        if (result is null)
            return Unauthorized(new { error = "No Azure DevOps access token linked for this user." });
        return Ok(result);
    }

    private string GetUserId() => User.RequireUserId();
}
