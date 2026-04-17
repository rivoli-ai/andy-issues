// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using ModelContextProtocol.Server;

namespace Andy.Issues.Api.Mcp;

[McpServerToolType]
public static class ServiceTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── Repositories ────────────────────────────────────────────────

    [McpServerTool, Description("List repositories accessible to the current user. Scope: mine (default), shared, or all.")]
    public static async Task<string> ListRepositories(
        IHttpContextAccessor ctx,
        IRepositoryService svc,
        [Description("Filter scope: mine, shared, or all")] string? scope,
        [Description("Page number (default 1)")] int? page,
        [Description("Page size (default 50)")] int? pageSize)
    {
        var userId = GetUserId(ctx);
        var parsed = scope?.ToLowerInvariant() switch
        {
            "shared" => RepositoryScope.Shared,
            "all" => RepositoryScope.All,
            _ => RepositoryScope.Mine
        };
        var result = await svc.ListAsync(userId, parsed, page ?? 1, pageSize ?? 50);
        return Serialize(result);
    }

    [McpServerTool, Description("Get details of a repository by its ID.")]
    public static async Task<string> GetRepository(
        IHttpContextAccessor ctx,
        IRepositoryService svc,
        [Description("Repository ID (GUID)")] string repositoryId)
    {
        var dto = await svc.GetAsync(Guid.Parse(repositoryId), GetUserId(ctx));
        return dto is null ? "Repository not found." : Serialize(dto);
    }

    [McpServerTool, Description("Sync repositories from GitHub. Provide a comma-separated list of full repo names (e.g. owner/repo).")]
    public static async Task<string> SyncGitHubRepositories(
        IHttpContextAccessor ctx,
        IRepositoryService svc,
        [Description("Comma-separated GitHub repo full names (owner/repo)")] string repoNames)
    {
        var names = repoNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var result = await svc.SyncFromGitHubAsync(GetUserId(ctx), names);
        return result is null ? "No linked GitHub provider found." : Serialize(result);
    }

    [McpServerTool, Description("Sync repositories from Azure DevOps for a given organization.")]
    public static async Task<string> SyncAzureDevOpsRepositories(
        IHttpContextAccessor ctx,
        IRepositoryService svc,
        [Description("Azure DevOps organization name")] string organization,
        [Description("Comma-separated Azure DevOps repository IDs")] string repoIds,
        [Description("Optional Azure DevOps project name")] string? project)
    {
        var ids = repoIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var result = await svc.SyncFromAzureDevOpsAsync(GetUserId(ctx), organization, project, ids);
        return result is null ? "No linked Azure DevOps provider found." : Serialize(result);
    }

    [McpServerTool, Description(
        "Register a repository by clone URL. Use this when the user already knows the clone URL — " +
        "it's the manual path that does not require a linked GitHub/Azure DevOps token. " +
        "When the user wants to import every repo they own from GitHub, call sync_github_repositories instead. " +
        "Calling this again with the same clone URL for the same user is a no-op and returns the existing repository.")]
    public static async Task<string> CreateRepository(
        IHttpContextAccessor ctx,
        IRepositoryService svc,
        [Description("Display name for the repository")] string name,
        [Description("Provider: 'github' or 'azuredevops'")] string provider,
        [Description("Absolute http(s) clone URL")] string cloneUrl,
        [Description("Optional description")] string? description,
        [Description("Optional default branch (defaults to 'main' server-side)")] string? defaultBranch,
        [Description("Optional provider-specific external ID (e.g. GitHub repo id, AzDO repo GUID)")] string? externalId)
    {
        var userId = GetUserId(ctx);
        var request = new CreateRepositoryRequest(name, description, provider, cloneUrl, defaultBranch, externalId);
        var (result, dto) = await svc.CreateAsync(request, userId);
        return result switch
        {
            CreateRepositoryResult.Created => Serialize(dto!),
            CreateRepositoryResult.AlreadyExists => Serialize(dto!),
            CreateRepositoryResult.InvalidProvider => $"Unknown provider '{provider}'. Use 'github' or 'azuredevops'.",
            CreateRepositoryResult.InvalidCloneUrl => "CloneUrl is required and must be an absolute http(s) URL.",
            _ => "Failed to create repository."
        };
    }

    [McpServerTool, Description("Delete a repository by its ID.")]
    public static async Task<string> DeleteRepository(
        IHttpContextAccessor ctx,
        IRepositoryService svc,
        [Description("Repository ID (GUID)")] string repositoryId)
    {
        var ok = await svc.DeleteAsync(Guid.Parse(repositoryId), GetUserId(ctx));
        return ok ? "Repository deleted." : "Repository not found or not owned by you.";
    }

    // ── Backlog ─────────────────────────────────────────────────────

    [McpServerTool, Description("Get the full backlog (epics, features, stories) for a repository.")]
    public static async Task<string> ListBacklog(
        IHttpContextAccessor ctx,
        IBacklogService svc,
        [Description("Repository ID (GUID)")] string repositoryId)
    {
        var dto = await svc.GetAsync(Guid.Parse(repositoryId), GetUserId(ctx));
        return dto is null ? "Repository not found or not accessible." : Serialize(dto);
    }

    [McpServerTool, Description("Create a new epic in a repository's backlog.")]
    public static async Task<string> CreateEpic(
        IHttpContextAccessor ctx,
        IBacklogService svc,
        [Description("Repository ID (GUID)")] string repositoryId,
        [Description("Epic title")] string title,
        [Description("Optional epic description")] string? description)
    {
        var request = new CreateEpicRequest(title, description, null, null);
        var dto = await svc.AddEpicAsync(Guid.Parse(repositoryId), request, GetUserId(ctx));
        return dto is null ? "Repository not found or not accessible." : Serialize(dto);
    }

    [McpServerTool, Description("Create a new feature under an epic.")]
    public static async Task<string> CreateFeature(
        IHttpContextAccessor ctx,
        IBacklogService svc,
        [Description("Epic ID (GUID)")] string epicId,
        [Description("Feature title")] string title,
        [Description("Optional feature description")] string? description)
    {
        var request = new CreateFeatureRequest(title, description, null, null);
        var dto = await svc.AddFeatureAsync(Guid.Parse(epicId), request, GetUserId(ctx));
        return dto is null ? "Epic not found or not accessible." : Serialize(dto);
    }

    [McpServerTool, Description("Create a new user story under a feature.")]
    public static async Task<string> CreateStory(
        IHttpContextAccessor ctx,
        IBacklogService svc,
        [Description("Feature ID (GUID)")] string featureId,
        [Description("Story title")] string title,
        [Description("Optional story description (user story format recommended)")] string? description,
        [Description("Optional acceptance criteria")] string? acceptanceCriteria,
        [Description("Optional story points estimate")] int? storyPoints)
    {
        var request = new CreateUserStoryRequest(title, description, acceptanceCriteria, storyPoints, null, null);
        var dto = await svc.AddStoryAsync(Guid.Parse(featureId), request, GetUserId(ctx));
        return dto is null ? "Feature not found or not accessible." : Serialize(dto);
    }

    [McpServerTool, Description("Update the status of a user story (e.g. Draft, Ready, InProgress, Done).")]
    public static async Task<string> UpdateStoryStatus(
        IHttpContextAccessor ctx,
        IBacklogService svc,
        [Description("Story ID (GUID)")] string storyId,
        [Description("New status (Draft, Ready, InProgress, Done)")] string status)
    {
        var request = new UpdateUserStoryStatusRequest(status, null);
        var result = await svc.UpdateStoryStatusAsync(Guid.Parse(storyId), request, GetUserId(ctx));
        return result.Outcome switch
        {
            UserStoryStatusUpdateOutcome.Updated => Serialize(result.Story!),
            _ => result.Error ?? result.Outcome.ToString()
        };
    }

    [McpServerTool, Description("Generate a draft backlog (epics, features, stories) for a repository using AI. Requires a linked LLM setting and code index.")]
    public static async Task<string> GenerateDraftBacklog(
        IHttpContextAccessor ctx,
        IDraftBacklogGenerator gen,
        [Description("Repository ID (GUID)")] string repositoryId)
    {
        var result = await gen.GenerateAsync(Guid.Parse(repositoryId), GetUserId(ctx));
        return result.Outcome switch
        {
            DraftBacklogOutcome.Generated => Serialize(result.Backlog!),
            _ => result.Error ?? result.Outcome.ToString()
        };
    }

    // ── Sandboxes ───────────────────────────────────────────────────

    [McpServerTool, Description("Create a new sandbox (container-based dev environment) for a repository branch.")]
    public static async Task<string> CreateSandbox(
        IHttpContextAccessor ctx,
        ISandboxService svc,
        [Description("Repository ID (GUID)")] string repositoryId,
        [Description("Branch name to check out in the sandbox")] string branch)
    {
        var request = new CreateSandboxRequest(Guid.Parse(repositoryId), branch, null);
        var dto = await svc.CreateAsync(request, GetUserId(ctx));
        return dto is null ? "Repository not found or not accessible." : Serialize(dto);
    }

    [McpServerTool, Description("List all sandboxes owned by the current user.")]
    public static async Task<string> ListSandboxes(
        IHttpContextAccessor ctx,
        ISandboxService svc)
    {
        var list = await svc.ListAsync(GetUserId(ctx));
        return Serialize(list);
    }

    [McpServerTool, Description("Get connection info (IDE, VNC, SSH endpoints) for a sandbox.")]
    public static async Task<string> GetSandboxConnection(
        IHttpContextAccessor ctx,
        ISandboxService svc,
        [Description("Sandbox ID (GUID)")] string sandboxId)
    {
        var dto = await svc.GetConnectionInfoAsync(Guid.Parse(sandboxId), GetUserId(ctx));
        return dto is null ? "Sandbox not found." : Serialize(dto);
    }

    [McpServerTool, Description("Destroy a sandbox by its ID.")]
    public static async Task<string> DestroySandbox(
        IHttpContextAccessor ctx,
        ISandboxService svc,
        [Description("Sandbox ID (GUID)")] string sandboxId)
    {
        var ok = await svc.DestroyAsync(Guid.Parse(sandboxId), GetUserId(ctx));
        return ok ? "Sandbox destroyed." : "Sandbox not found.";
    }

    // ── MCP configs ─────────────────────────────────────────────────

    [McpServerTool, Description("List MCP server configurations visible to the current user (personal + shared).")]
    public static async Task<string> ListMcpConfigs(
        IHttpContextAccessor ctx,
        IMcpConfigService svc)
    {
        var list = await svc.ListForUserAsync(GetUserId(ctx));
        return Serialize(list);
    }

    // ── LLM settings ────────────────────────────────────────────────

    [McpServerTool, Description("List the current user's saved LLM provider/model configurations.")]
    public static async Task<string> ListLlmSettings(
        IHttpContextAccessor ctx,
        ILlmSettingService svc)
    {
        var list = await svc.ListAsync(GetUserId(ctx));
        return Serialize(list);
    }

    [McpServerTool, Description(
        "Create an LLM setting owned by the current user. " +
        "Provider must be one of: openai, anthropic, ollama, custom. " +
        "The ApiKey is stored via the secret store and never echoed back in responses.")]
    public static async Task<string> CreateLlmSetting(
        IHttpContextAccessor ctx,
        ILlmSettingService svc,
        [Description("Display name (1-100 chars)")] string name,
        [Description("Provider: openai, anthropic, ollama, or custom")] string provider,
        [Description("Model id, e.g. gpt-4o or claude-opus-4-6")] string model,
        [Description("API key — stored encrypted, never returned")] string apiKey,
        [Description("Optional base URL override (absolute http(s))")] string? baseUrl,
        [Description("Mark this setting as the user's default")] bool? isDefault)
    {
        var (result, dto) = await svc.CreateAsync(
            new CreateLlmSettingRequest(name, provider, apiKey, model, baseUrl, isDefault),
            GetUserId(ctx));
        return result switch
        {
            CreateLlmSettingResult.Created => Serialize(dto!),
            CreateLlmSettingResult.InvalidProvider => $"Unknown provider '{provider}'. Use openai, anthropic, ollama, or custom.",
            CreateLlmSettingResult.InvalidBaseUrl => "BaseUrl must be an absolute http(s) URL when supplied.",
            _ => "Failed to create LLM setting."
        };
    }

    [McpServerTool, Description("Update an LLM setting owned by the current user. Null/empty fields are left untouched.")]
    public static async Task<string> UpdateLlmSetting(
        IHttpContextAccessor ctx,
        ILlmSettingService svc,
        [Description("LLM setting ID (GUID)")] string settingId,
        [Description("Optional new display name")] string? name,
        [Description("Optional new provider")] string? provider,
        [Description("Optional new model id")] string? model,
        [Description("Optional new API key (replaces the stored secret)")] string? apiKey,
        [Description("Optional new base URL")] string? baseUrl,
        [Description("Set this setting as the user's default")] bool? isDefault)
    {
        var (result, dto) = await svc.UpdateAsync(
            Guid.Parse(settingId),
            new UpdateLlmSettingRequest(name, provider, apiKey, model, baseUrl, isDefault),
            GetUserId(ctx));
        return result switch
        {
            UpdateLlmSettingResult.Updated => Serialize(dto!),
            UpdateLlmSettingResult.NotFound => "LLM setting not found.",
            UpdateLlmSettingResult.InvalidProvider => $"Unknown provider '{provider}'. Use openai, anthropic, ollama, or custom.",
            UpdateLlmSettingResult.InvalidBaseUrl => "BaseUrl must be an absolute http(s) URL when supplied.",
            _ => "Failed to update LLM setting."
        };
    }

    [McpServerTool, Description("Delete an LLM setting owned by the current user.")]
    public static async Task<string> DeleteLlmSetting(
        IHttpContextAccessor ctx,
        ILlmSettingService svc,
        [Description("LLM setting ID (GUID)")] string settingId)
    {
        var ok = await svc.DeleteAsync(Guid.Parse(settingId), GetUserId(ctx));
        return ok ? "LLM setting deleted." : "LLM setting not found.";
    }

    [McpServerTool, Description("Mark an LLM setting as the user's default. Any other default is cleared atomically.")]
    public static async Task<string> SetDefaultLlmSetting(
        IHttpContextAccessor ctx,
        ILlmSettingService svc,
        [Description("LLM setting ID (GUID)")] string settingId)
    {
        var ok = await svc.SetDefaultAsync(Guid.Parse(settingId), GetUserId(ctx));
        return ok ? "Default LLM setting updated." : "LLM setting not found.";
    }

    // ── Artifact feeds ──────────────────────────────────────────────

    [McpServerTool, Description("List enabled artifact feeds (NuGet/npm registries) configured for this service.")]
    public static async Task<string> ListEnabledArtifactFeeds(
        IArtifactFeedService svc)
    {
        var list = await svc.GetEnabledAsync();
        return Serialize(list);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static string GetUserId(IHttpContextAccessor ctx)
    {
        var user = ctx.HttpContext?.User;
        return user?.FindFirst("sub")?.Value
            ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user?.Identity?.Name
            ?? "dev-user";
    }

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);
}
