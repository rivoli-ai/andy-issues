// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Security.Claims;
using System.Text.Json;
using Andy.Issues.Api.Mcp;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Andy.Issues.Tests.Unit.Mcp;

public class ServiceToolsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpContextAccessor _ctx;

    public ServiceToolsTests()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", "test-user")
        }, "test");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        _ctx = new HttpContextAccessor { HttpContext = httpContext };
    }

    // ── Repositories ────────────────────────────────────────────────

    [Fact]
    public async Task ListRepositories_DelegatesToService()
    {
        var svc = new StubRepositoryService
        {
            ListResult = new PagedResult<RepositoryDto>(new List<RepositoryDto>(), 1, 50, 0)
        };

        var json = await ServiceTools.ListRepositories(_ctx, svc, "mine", 1, 50);
        var result = JsonSerializer.Deserialize<PagedResult<RepositoryDto>>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetRepository_ReturnsNotFound_WhenNull()
    {
        var svc = new StubRepositoryService { GetResult = null };

        var result = await ServiceTools.GetRepository(_ctx, svc, Guid.NewGuid().ToString());

        Assert.Equal("Repository not found.", result);
    }

    [Fact]
    public async Task GetRepository_ReturnsJson_WhenFound()
    {
        var dto = MakeRepoDto("test-repo");
        var svc = new StubRepositoryService { GetResult = dto };

        var json = await ServiceTools.GetRepository(_ctx, svc, dto.Id.ToString());
        var result = JsonSerializer.Deserialize<RepositoryDto>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal("test-repo", result.Name);
    }

    [Fact]
    public async Task CreateRepository_DelegatesRequestAndReturnsDto()
    {
        var dto = MakeRepoDto("new-repo");
        var svc = new StubRepositoryService
        {
            CreateResult = CreateRepositoryResult.Created,
            CreateDto = dto
        };

        var json = await ServiceTools.CreateRepository(
            _ctx, svc,
            name: "new-repo",
            provider: "github",
            cloneUrl: "https://github.com/acme/new-repo.git",
            description: "manual add",
            defaultBranch: "main",
            externalId: "123");

        var result = JsonSerializer.Deserialize<RepositoryDto>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal("new-repo", result.Name);
        Assert.NotNull(svc.LastCreateRequest);
        Assert.Equal("new-repo", svc.LastCreateRequest.Name);
        Assert.Equal("github", svc.LastCreateRequest.Provider);
        Assert.Equal("https://github.com/acme/new-repo.git", svc.LastCreateRequest.CloneUrl);
        Assert.Equal("manual add", svc.LastCreateRequest.Description);
        Assert.Equal("main", svc.LastCreateRequest.DefaultBranch);
        Assert.Equal("123", svc.LastCreateRequest.ExternalId);
        Assert.Equal("test-user", svc.LastCreateOwner);
    }

    [Fact]
    public async Task CreateRepository_AlreadyExists_ReturnsExistingDto()
    {
        var existing = MakeRepoDto("dup-repo");
        var svc = new StubRepositoryService
        {
            CreateResult = CreateRepositoryResult.AlreadyExists,
            CreateDto = existing
        };

        var json = await ServiceTools.CreateRepository(
            _ctx, svc, "dup-repo", "github", "https://github.com/acme/dup-repo.git", null, null, null);

        var result = JsonSerializer.Deserialize<RepositoryDto>(json, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(existing.Id, result.Id);
    }

    [Fact]
    public async Task CreateRepository_InvalidProvider_ReturnsErrorMessage()
    {
        var svc = new StubRepositoryService { CreateResult = CreateRepositoryResult.InvalidProvider };

        var result = await ServiceTools.CreateRepository(
            _ctx, svc, "x", "bitbucket", "https://example.com/x.git", null, null, null);

        Assert.Contains("bitbucket", result);
        Assert.Contains("github", result);
    }

    [Fact]
    public async Task CreateRepository_InvalidCloneUrl_ReturnsErrorMessage()
    {
        var svc = new StubRepositoryService { CreateResult = CreateRepositoryResult.InvalidCloneUrl };

        var result = await ServiceTools.CreateRepository(
            _ctx, svc, "x", "github", "not-a-url", null, null, null);

        Assert.Contains("CloneUrl", result);
    }

    [Fact]
    public async Task SyncGitHubRepositories_DelegatesToService()
    {
        var svc = new StubRepositoryService
        {
            SyncGitHubResult = new SyncResult(1, 0, 0, new List<string>())
        };

        var json = await ServiceTools.SyncGitHubRepositories(_ctx, svc, "owner/repo1, owner/repo2");
        var result = JsonSerializer.Deserialize<SyncResult>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal(1, result.Added);
        Assert.Equal(2, svc.LastGitHubFullNames!.Count);
    }

    [Fact]
    public async Task SyncGitHubRepositories_ReturnsMessage_WhenNoProvider()
    {
        var svc = new StubRepositoryService { SyncGitHubResult = null };

        var result = await ServiceTools.SyncGitHubRepositories(_ctx, svc, "owner/repo");

        Assert.Equal("No linked GitHub provider found.", result);
    }

    [Fact]
    public async Task DeleteRepository_ReturnsDeleted()
    {
        var svc = new StubRepositoryService { DeleteResult = true };

        var result = await ServiceTools.DeleteRepository(_ctx, svc, Guid.NewGuid().ToString());

        Assert.Equal("Repository deleted.", result);
    }

    [Fact]
    public async Task DeleteRepository_ReturnsNotFound()
    {
        var svc = new StubRepositoryService { DeleteResult = false };

        var result = await ServiceTools.DeleteRepository(_ctx, svc, Guid.NewGuid().ToString());

        Assert.Contains("not found", result);
    }

    // ── Backlog ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListBacklog_ReturnsJson()
    {
        var repoId = Guid.NewGuid();
        var svc = new StubBacklogService
        {
            GetResult = new BacklogDto(repoId, new List<EpicDto>())
        };

        var json = await ServiceTools.ListBacklog(_ctx, svc, repoId.ToString());
        var result = JsonSerializer.Deserialize<BacklogDto>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal(repoId, result.RepositoryId);
    }

    [Fact]
    public async Task CreateEpic_DelegatesToService()
    {
        var epicDto = MakeEpicDto("New Epic");
        var svc = new StubBacklogService { AddEpicResult = epicDto };

        var json = await ServiceTools.CreateEpic(_ctx, svc, Guid.NewGuid().ToString(), "New Epic", "Description");

        Assert.Contains("New Epic", json);
    }

    [Fact]
    public async Task CreateFeature_DelegatesToService()
    {
        var dto = MakeFeatureDto("New Feature");
        var svc = new StubBacklogService { AddFeatureResult = dto };

        var json = await ServiceTools.CreateFeature(_ctx, svc, Guid.NewGuid().ToString(), "New Feature", null);

        Assert.Contains("New Feature", json);
    }

    [Fact]
    public async Task CreateStory_DelegatesToService()
    {
        var dto = MakeStoryDto("New Story");
        var svc = new StubBacklogService { AddStoryResult = dto };

        var json = await ServiceTools.CreateStory(
            _ctx, svc, Guid.NewGuid().ToString(), "New Story", "desc", "AC", 3);

        Assert.Contains("New Story", json);
    }

    [Fact]
    public async Task UpdateStoryStatus_ReturnsUpdatedStory()
    {
        var dto = MakeStoryDto("S1", "Ready");
        var svc = new StubBacklogService
        {
            UpdateStoryStatusResult = UserStoryStatusUpdateResult.Ok(dto)
        };

        var json = await ServiceTools.UpdateStoryStatus(_ctx, svc, dto.Id.ToString(), "Ready");

        Assert.Contains("Ready", json);
    }

    [Fact]
    public async Task UpdateStoryStatus_ReturnsError_WhenInvalid()
    {
        var svc = new StubBacklogService
        {
            UpdateStoryStatusResult = UserStoryStatusUpdateResult.InvalidStatus("BadStatus")
        };

        var result = await ServiceTools.UpdateStoryStatus(_ctx, svc, Guid.NewGuid().ToString(), "BadStatus");

        Assert.Contains("BadStatus", result);
    }

    [Fact]
    public async Task GenerateDraftBacklog_ReturnsBacklog()
    {
        var repoId = Guid.NewGuid();
        var gen = new StubDraftBacklogGenerator
        {
            Result = new DraftBacklogResult(DraftBacklogOutcome.Generated,
                new BacklogDto(repoId, new List<EpicDto>()), null)
        };

        var json = await ServiceTools.GenerateDraftBacklog(_ctx, gen, repoId.ToString());

        Assert.Contains(repoId.ToString(), json);
    }

    [Fact]
    public async Task GenerateDraftBacklog_ReturnsError_WhenFailed()
    {
        var gen = new StubDraftBacklogGenerator
        {
            Result = new DraftBacklogResult(DraftBacklogOutcome.NoLlmSetting, null, "No LLM configured")
        };

        var result = await ServiceTools.GenerateDraftBacklog(_ctx, gen, Guid.NewGuid().ToString());

        Assert.Equal("No LLM configured", result);
    }

    // ── Sandboxes ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateSandbox_ReturnsJson()
    {
        var dto = MakeSandboxDto();
        var svc = new StubSandboxService { CreateResult = dto };

        var json = await ServiceTools.CreateSandbox(_ctx, svc, dto.RepositoryId.ToString(), "main");

        Assert.Contains("main", json);
    }

    [Fact]
    public async Task ListSandboxes_ReturnsJson()
    {
        var svc = new StubSandboxService { ListResult = new List<SandboxDto>() };

        var json = await ServiceTools.ListSandboxes(_ctx, svc);

        Assert.Equal("[]", json);
    }

    [Fact]
    public async Task GetSandboxConnection_ReturnsNotFound_WhenNull()
    {
        var svc = new StubSandboxService { ConnectionResult = null };

        var result = await ServiceTools.GetSandboxConnection(_ctx, svc, Guid.NewGuid().ToString());

        Assert.Equal("Sandbox not found.", result);
    }

    [Fact]
    public async Task DestroySandbox_ReturnsDestroyed()
    {
        var svc = new StubSandboxService { DestroyResult = true };

        var result = await ServiceTools.DestroySandbox(_ctx, svc, Guid.NewGuid().ToString());

        Assert.Equal("Sandbox destroyed.", result);
    }

    // ── MCP configs ─────────────────────────────────────────────────

    [Fact]
    public async Task ListMcpConfigs_DelegatesToService()
    {
        var svc = new StubMcpConfigService
        {
            ListResult = new List<McpServerConfigDto>()
        };

        var json = await ServiceTools.ListMcpConfigs(_ctx, svc);

        Assert.Equal("[]", json);
    }

    // ── Artifact feeds ──────────────────────────────────────────────

    [Fact]
    public async Task ListEnabledArtifactFeeds_DelegatesToService()
    {
        var svc = new StubArtifactFeedService
        {
            EnabledResult = new List<ArtifactFeedConfigDto>()
        };

        var json = await ServiceTools.ListEnabledArtifactFeeds(svc);

        Assert.Equal("[]", json);
    }

    // ── Issues / Triage (Z9) ─────────────────────────────────────────

    [Fact]
    public async Task IssueGet_ReturnsNotFound_WhenNull()
    {
        var svc = new StubIssueService { GetResult = null };

        var result = await ServiceTools.IssueGet(_ctx, svc, Guid.NewGuid().ToString());

        Assert.Equal("Issue not found.", result);
    }

    [Fact]
    public async Task IssueGet_ReturnsJson_WhenFound()
    {
        var dto = MakeIssueDto("intake");
        var svc = new StubIssueService { GetResult = dto };

        var json = await ServiceTools.IssueGet(_ctx, svc, dto.Id.ToString());
        var roundTripped = JsonSerializer.Deserialize<IssueDto>(json, JsonOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal("intake", roundTripped.Title);
        Assert.Equal("test-user", svc.LastGetUser);
    }

    [Fact]
    public async Task IssueList_DelegatesFilter_AndReturnsPagedResult()
    {
        var svc = new StubIssueService
        {
            ListResult = new PagedResult<IssueDto>(new List<IssueDto> { MakeIssueDto("a") }, 1, 50, 1)
        };

        var json = await ServiceTools.IssueList(_ctx, svc, "Triaged", 1, 50);
        var result = JsonSerializer.Deserialize<PagedResult<IssueDto>>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Triaged", svc.LastListFilter);
        Assert.Equal(1, svc.LastListPage);
        Assert.Equal(50, svc.LastListPageSize);
    }

    [Fact]
    public async Task IssueList_DefaultsPagingWhenOmitted()
    {
        var svc = new StubIssueService
        {
            ListResult = new PagedResult<IssueDto>(new List<IssueDto>(), 1, 50, 0)
        };

        await ServiceTools.IssueList(_ctx, svc, null, null, null);

        Assert.Equal(1, svc.LastListPage);
        Assert.Equal(50, svc.LastListPageSize);
        Assert.Null(svc.LastListFilter);
    }

    [Fact]
    public async Task IssueTriage_ReturnsUpdatedIssue_OnSuccess()
    {
        var dto = MakeIssueDto("intake", "Triaging");
        var svc = new StubIssueService
        {
            StartTriageResult = IssueTriageResult.Ok(dto)
        };

        var json = await ServiceTools.IssueTriage(_ctx, svc, dto.Id.ToString());

        Assert.Contains("Triaging", json);
        Assert.Equal(dto.Id, svc.LastStartTriageId);
    }

    [Fact]
    public async Task IssueTriage_ReturnsError_OnInvalidTransition()
    {
        var svc = new StubIssueService
        {
            StartTriageResult = IssueTriageResult.InvalidTransition(
                "Invalid triage transition: Accepted → Triaging.")
        };

        var result = await ServiceTools.IssueTriage(_ctx, svc, Guid.NewGuid().ToString());

        Assert.Contains("Accepted", result);
    }

    [Fact]
    public async Task IssueTriage_ReturnsNotFound_WhenMissing()
    {
        var svc = new StubIssueService { StartTriageResult = IssueTriageResult.NotFound() };

        var result = await ServiceTools.IssueTriage(_ctx, svc, Guid.NewGuid().ToString());

        Assert.Equal("Issue not found.", result);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static IssueDto MakeIssueDto(string title, string state = "NeedsTriage") =>
        new(Guid.NewGuid(), "test-user", null, title, null, state, null, null,
            null, DateTimeOffset.UtcNow, null);

    private static RepositoryDto MakeRepoDto(string name) =>
        new(Guid.NewGuid(), "test-user", name, null, "GitHub", "https://github.com/test/repo.git",
            "main", null, null, false, "None", DateTimeOffset.UtcNow, null);

    private static EpicDto MakeEpicDto(string title) =>
        new(Guid.NewGuid(), "EPIC-0", Guid.NewGuid(), title, null, 0, null,
            DateTimeOffset.UtcNow, null, new List<FeatureDto>());

    private static FeatureDto MakeFeatureDto(string title) =>
        new(Guid.NewGuid(), "FEAT-0", Guid.NewGuid(), title, null, 0, null,
            DateTimeOffset.UtcNow, null, new List<UserStoryDto>());

    private static UserStoryDto MakeStoryDto(string title, string status = "Draft") =>
        new(Guid.NewGuid(), "STORY-0", Guid.NewGuid(), title, null, null, null, status, null, 0, null, null,
            DateTimeOffset.UtcNow, null);

    private static SandboxDto MakeSandboxDto() =>
        new(Guid.NewGuid(), "ctr-1", Guid.NewGuid(), "test-user", "main", "Running",
            null, null, DateTimeOffset.UtcNow, null);
}

// ── Stubs ────────────────────────────────────────────────────────────

file class StubRepositoryService : IRepositoryService
{
    public PagedResult<RepositoryDto>? ListResult { get; set; }
    public RepositoryDto? GetResult { get; set; }
    public bool DeleteResult { get; set; }
    public SyncResult? SyncGitHubResult { get; set; }
    public SyncResult? SyncAzureResult { get; set; }
    public IReadOnlyList<string>? LastGitHubFullNames { get; private set; }
    public CreateRepositoryResult CreateResult { get; set; } = CreateRepositoryResult.Created;
    public RepositoryDto? CreateDto { get; set; }
    public CreateRepositoryRequest? LastCreateRequest { get; private set; }
    public string? LastCreateOwner { get; private set; }

    public Task<PagedResult<RepositoryDto>> ListAsync(string userId, RepositoryScope scope, int page, int pageSize, CancellationToken ct = default) =>
        Task.FromResult(ListResult!);

    public Task<RepositoryDto?> GetAsync(Guid id, string userId, CancellationToken ct = default) =>
        Task.FromResult(GetResult);

    public Task<bool> DeleteAsync(Guid id, string userId, CancellationToken ct = default) =>
        Task.FromResult(DeleteResult);

    public Task<SyncResult?> SyncFromGitHubAsync(string userId, IReadOnlyList<string> fullNames, CancellationToken ct = default)
    {
        LastGitHubFullNames = fullNames;
        return Task.FromResult(SyncGitHubResult);
    }

    public Task<SyncResult?> SyncFromAzureDevOpsAsync(string userId, string organization, string? project, IReadOnlyList<string> repositoryIds, CancellationToken ct = default) =>
        Task.FromResult(SyncAzureResult);

    public Task<(CreateRepositoryResult Result, RepositoryDto? Dto)> CreateAsync(CreateRepositoryRequest request, string ownerUserId, CancellationToken ct = default)
    {
        LastCreateRequest = request;
        LastCreateOwner = ownerUserId;
        return Task.FromResult((CreateResult, CreateDto));
    }

    public Task<(ShareResult Result, RepositoryShareDto? Dto)> ShareAsync(Guid repositoryId, string email, string ownerUserId, CancellationToken ct = default) =>
        Task.FromResult<(ShareResult, RepositoryShareDto?)>((ShareResult.Created, null));

    public Task<bool> UnshareAsync(Guid repositoryId, string targetUserId, string ownerUserId, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<IReadOnlyList<RepositoryShareDto>?> ListSharesAsync(Guid repositoryId, string ownerUserId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RepositoryShareDto>?>(null);

    public Task<SetLlmResult> SetLlmSettingAsync(Guid repositoryId, Guid? llmSettingId, string ownerUserId, CancellationToken ct = default) =>
        Task.FromResult(SetLlmResult.Updated);

    public Task<SetAzureIdentityResult> SetAzureIdentityAsync(Guid repositoryId, string clientId, string clientSecret, string tenantId, string? subscriptionId, string ownerUserId, CancellationToken ct = default) =>
        Task.FromResult(SetAzureIdentityResult.Updated);

    public Task<SetAzureIdentityResult> SetAzurePatIdentityAsync(Guid repositoryId, string organization, string project, string pat, string ownerUserId, CancellationToken ct = default) =>
        Task.FromResult(SetAzureIdentityResult.Updated);

    public Task<VerifyAzureIdentityResult?> VerifyAzureIdentityAsync(Guid repositoryId, string ownerUserId, CancellationToken ct = default) =>
        Task.FromResult<VerifyAzureIdentityResult?>(null);
}

file class StubBacklogService : IBacklogService
{
    public BacklogDto? GetResult { get; set; }
    public EpicDto? AddEpicResult { get; set; }
    public FeatureDto? AddFeatureResult { get; set; }
    public UserStoryDto? AddStoryResult { get; set; }
    public UserStoryStatusUpdateResult? UpdateStoryStatusResult { get; set; }

    public Task<BacklogDto?> GetAsync(Guid repositoryId, string userId, CancellationToken ct = default) =>
        Task.FromResult(GetResult);

    public Task<EpicDto?> GetEpicAsync(string identifier, string userId, CancellationToken ct = default) =>
        Task.FromResult<EpicDto?>(null);

    public Task<FeatureDto?> GetFeatureAsync(string identifier, string userId, CancellationToken ct = default) =>
        Task.FromResult<FeatureDto?>(null);

    public Task<UserStoryDto?> GetStoryAsync(string identifier, string userId, CancellationToken ct = default) =>
        Task.FromResult<UserStoryDto?>(null);

    public Task<EpicDto?> AddEpicAsync(Guid repositoryId, CreateEpicRequest request, string userId, CancellationToken ct = default) =>
        Task.FromResult(AddEpicResult);

    public Task<EpicDto?> UpdateEpicAsync(Guid epicId, UpdateEpicRequest request, string userId, CancellationToken ct = default) =>
        Task.FromResult<EpicDto?>(null);

    public Task<bool> DeleteEpicAsync(Guid epicId, string userId, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<FeatureDto?> AddFeatureAsync(Guid epicId, CreateFeatureRequest request, string userId, CancellationToken ct = default) =>
        Task.FromResult(AddFeatureResult);

    public Task<FeatureDto?> UpdateFeatureAsync(Guid featureId, UpdateFeatureRequest request, string userId, CancellationToken ct = default) =>
        Task.FromResult<FeatureDto?>(null);

    public Task<bool> DeleteFeatureAsync(Guid featureId, string userId, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<UserStoryDto?> AddStoryAsync(Guid featureId, CreateUserStoryRequest request, string userId, CancellationToken ct = default) =>
        Task.FromResult(AddStoryResult);

    public Task<UserStoryDto?> UpdateStoryAsync(Guid storyId, UpdateUserStoryRequest request, string userId, CancellationToken ct = default) =>
        Task.FromResult<UserStoryDto?>(null);

    public Task<UserStoryStatusUpdateResult> UpdateStoryStatusAsync(Guid storyId, UpdateUserStoryStatusRequest request, string userId, CancellationToken ct = default) =>
        Task.FromResult(UpdateStoryStatusResult!);

    public Task<bool> DeleteStoryAsync(Guid storyId, string userId, CancellationToken ct = default) =>
        Task.FromResult(true);
}

file class StubIssueService : IIssueService
{
    public IssueDto? GetResult { get; set; }
    public PagedResult<IssueDto>? ListResult { get; set; }
    public IssueTriageResult? StartTriageResult { get; set; }

    public string? LastGetUser { get; private set; }
    public string? LastListFilter { get; private set; }
    public int LastListPage { get; private set; }
    public int LastListPageSize { get; private set; }
    public Guid LastStartTriageId { get; private set; }

    public Task<IssueDto?> GetAsync(Guid id, string userId, CancellationToken ct = default)
    {
        LastGetUser = userId;
        return Task.FromResult(GetResult);
    }

    public Task<PagedResult<IssueDto>> ListAsync(
        string userId, string? triageState, int page, int pageSize, CancellationToken ct = default)
    {
        LastListFilter = triageState;
        LastListPage = page;
        LastListPageSize = pageSize;
        return Task.FromResult(ListResult!);
    }

    public Task<IssueTriageResult> StartTriageAsync(Guid id, string userId, CancellationToken ct = default)
    {
        LastStartTriageId = id;
        return Task.FromResult(StartTriageResult!);
    }

    public Task<IssueTriageResult> CompleteTriageAsync(Guid id, string userId, Andy.Issues.Domain.ValueTypes.TriageOutput? output = null, CancellationToken ct = default) =>
        Task.FromResult(IssueTriageResult.NotFound());

    public Task<IssueTriageResult> AcceptAsync(Guid id, string userId, CancellationToken ct = default) =>
        Task.FromResult(IssueTriageResult.NotFound());

    public Task<IssueTriageResult> RejectAsync(Guid id, string userId, CancellationToken ct = default) =>
        Task.FromResult(IssueTriageResult.NotFound());

    public Task<IssueDto> CreateAsync(CreateIssueRequest request, string userId, CancellationToken ct = default) =>
        Task.FromResult(new IssueDto(Guid.NewGuid(), userId, null, request.Title, request.Body,
            "NeedsTriage", null, null, null, DateTimeOffset.UtcNow, null));
}

file class StubDraftBacklogGenerator : IDraftBacklogGenerator
{
    public DraftBacklogResult? Result { get; set; }

    public Task<DraftBacklogResult> GenerateAsync(Guid repositoryId, string userId, CancellationToken ct = default) =>
        Task.FromResult(Result!);
}

file class StubSandboxService : ISandboxService
{
    public SandboxDto? CreateResult { get; set; }
    public IReadOnlyList<SandboxDto>? ListResult { get; set; }
    public SandboxDto? GetResult { get; set; }
    public bool DestroyResult { get; set; }
    public SandboxConnectionDto? ConnectionResult { get; set; }

    public Task<SandboxDto?> CreateAsync(CreateSandboxRequest request, string userId, CancellationToken ct = default) =>
        Task.FromResult(CreateResult);

    public Task<IReadOnlyList<SandboxDto>> ListAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult(ListResult!);

    public Task<SandboxDto?> GetAsync(Guid sandboxId, string userId, CancellationToken ct = default) =>
        Task.FromResult(GetResult);

    public Task<bool> DestroyAsync(Guid sandboxId, string userId, CancellationToken ct = default) =>
        Task.FromResult(DestroyResult);

    public Task<SandboxConnectionDto?> GetConnectionInfoAsync(Guid sandboxId, string userId, CancellationToken ct = default) =>
        Task.FromResult(ConnectionResult);
}

file class StubMcpConfigService : IMcpConfigService
{
    public IReadOnlyList<McpServerConfigDto>? ListResult { get; set; }

    public Task<IReadOnlyList<McpServerConfigDto>> ListForUserAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult(ListResult!);

    public Task<IReadOnlyList<McpServerConfigFull>> GetEnabledForUserAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<McpServerConfigFull>>(new List<McpServerConfigFull>());

    public Task<McpServerConfigDto?> GetAsync(Guid id, string userId, bool isAdmin, CancellationToken ct = default) =>
        Task.FromResult<McpServerConfigDto?>(null);

    public Task<McpConfigResult> CreateAsync(CreateMcpServerConfigRequest request, string userId, bool isAdmin, CancellationToken ct = default) =>
        Task.FromResult(new McpConfigResult(McpConfigOutcome.Ok, null, null));

    public Task<McpConfigResult> UpdateAsync(Guid id, UpdateMcpServerConfigRequest request, string userId, bool isAdmin, CancellationToken ct = default) =>
        Task.FromResult(new McpConfigResult(McpConfigOutcome.Ok, null, null));

    public Task<McpConfigResult> ToggleAsync(Guid id, string userId, bool isAdmin, CancellationToken ct = default) =>
        Task.FromResult(new McpConfigResult(McpConfigOutcome.Ok, null, null));

    public Task<McpConfigOutcome> DeleteAsync(Guid id, string userId, bool isAdmin, CancellationToken ct = default) =>
        Task.FromResult(McpConfigOutcome.Ok);

    public Task<McpToolDiscoveryEndpointResult> DiscoverToolsAsync(Guid id, string userId, bool isAdmin, CancellationToken ct = default) =>
        Task.FromResult(new McpToolDiscoveryEndpointResult(McpToolDiscoveryEndpointOutcome.Ok, null, null, null));
}

file class StubArtifactFeedService : IArtifactFeedService
{
    public IReadOnlyList<ArtifactFeedConfigDto>? EnabledResult { get; set; }

    public Task<IReadOnlyList<ArtifactFeedConfigDto>> GetEnabledAsync(CancellationToken ct = default) =>
        Task.FromResult(EnabledResult!);

    public Task<IReadOnlyList<ArtifactFeedConfigDto>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ArtifactFeedConfigDto>>(new List<ArtifactFeedConfigDto>());

    public Task<ArtifactFeedConfigDto?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult<ArtifactFeedConfigDto?>(null);

    public Task<ArtifactFeedResult> CreateAsync(CreateArtifactFeedConfigRequest request, CancellationToken ct = default) =>
        Task.FromResult(new ArtifactFeedResult(ArtifactFeedOutcome.Ok, null, null));

    public Task<ArtifactFeedResult> UpdateAsync(Guid id, UpdateArtifactFeedConfigRequest request, CancellationToken ct = default) =>
        Task.FromResult(new ArtifactFeedResult(ArtifactFeedOutcome.Ok, null, null));

    public Task<ArtifactFeedOutcome> DeleteAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(ArtifactFeedOutcome.Ok);

    public Task<ArtifactFeedBrowseResult> BrowseAzureDevOpsFeedsAsync(string userId, string organization, CancellationToken ct = default) =>
        Task.FromResult(new ArtifactFeedBrowseResult(ArtifactFeedBrowseOutcome.Ok, null, null));
}
