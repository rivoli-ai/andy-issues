// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Cli;

/// <summary>
/// Integration tests that exercise the REST API routes used by the CLI,
/// validating the HTTP round-trip (URL paths, serialization, status codes).
/// </summary>
public class CliIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CliIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Repos ───────────────────────────────────────────────────────

    [Fact]
    public async Task Repos_List_ReturnsRepositories()
    {
        await SeedRepoAsync("cli-list-test");

        var result = await _client.GetFromJsonAsync<PagedResult<RepositoryDto>>(
            "api/repositories?scope=mine&page=1&pageSize=50", JsonOptions);

        Assert.NotNull(result);
        Assert.Contains(result.Items, r => r.Name == "cli-list-test");
    }

    [Fact]
    public async Task Repos_Get_ReturnsRepository()
    {
        var repoId = await SeedRepoAsync("cli-get-test");

        var repo = await _client.GetFromJsonAsync<RepositoryDto>(
            $"api/repositories/{repoId}", JsonOptions);

        Assert.NotNull(repo);
        Assert.Equal("cli-get-test", repo.Name);
    }

    [Fact]
    public async Task Repos_Add_CreatesRepositoryAndAppearsInList()
    {
        var cloneUrl = $"https://github.com/acme/cli-add-{Guid.NewGuid():N}.git";
        var request = new CreateRepositoryRequest(
            Name: "cli-add-test",
            Description: "via CLI",
            Provider: "github",
            CloneUrl: cloneUrl,
            DefaultBranch: null,
            ExternalId: null);

        var createResp = await _client.PostAsJsonAsync("api/repositories", request, JsonOptions);
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<RepositoryDto>(JsonOptions);

        Assert.NotNull(created);
        Assert.Equal("cli-add-test", created.Name);
        Assert.Equal(cloneUrl, created.CloneUrl);

        var list = await _client.GetFromJsonAsync<PagedResult<RepositoryDto>>(
            "api/repositories?scope=mine&page=1&pageSize=50", JsonOptions);
        Assert.NotNull(list);
        Assert.Contains(list.Items, r => r.Id == created.Id);
    }

    [Fact]
    public async Task Repos_Add_DuplicateCloneUrl_IsIdempotent()
    {
        var cloneUrl = $"https://github.com/acme/cli-dup-{Guid.NewGuid():N}.git";
        var request = new CreateRepositoryRequest(
            "cli-dup", null, "github", cloneUrl, null, null);

        var first = await _client.PostAsJsonAsync("api/repositories", request, JsonOptions);
        first.EnsureSuccessStatusCode();
        var firstDto = await first.Content.ReadFromJsonAsync<RepositoryDto>(JsonOptions);

        var second = await _client.PostAsJsonAsync("api/repositories", request, JsonOptions);
        second.EnsureSuccessStatusCode();
        var secondDto = await second.Content.ReadFromJsonAsync<RepositoryDto>(JsonOptions);

        Assert.Equal(firstDto!.Id, secondDto!.Id);
    }

    [Fact]
    public async Task Repos_Add_InvalidProvider_Returns400()
    {
        var request = new CreateRepositoryRequest(
            "bad", null, "bitbucket", "https://example.com/x.git", null, null);

        var resp = await _client.PostAsJsonAsync("api/repositories", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Repos_Delete_Succeeds()
    {
        var repoId = await SeedRepoAsync("cli-delete-test");

        var response = await _client.DeleteAsync($"api/repositories/{repoId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Repos_Share_SharesRepository()
    {
        var repoId = await SeedRepoAsync("cli-share-test");
        await SeedUserAsync("cli-share-target@test.local");

        var response = await _client.PostAsJsonAsync(
            $"api/repositories/{repoId}/share",
            new ShareRepositoryRequest("cli-share-target@test.local"), JsonOptions);

        response.EnsureSuccessStatusCode();
        var share = await response.Content.ReadFromJsonAsync<RepositoryShareDto>(JsonOptions);
        Assert.NotNull(share);
        Assert.Equal(repoId, share.RepositoryId);
    }

    // ── Backlog ─────────────────────────────────────────────────────

    [Fact]
    public async Task Backlog_List_ReturnsBacklog()
    {
        var repoId = await SeedRepoWithBacklogAsync("cli-backlog-test");

        var backlog = await _client.GetFromJsonAsync<BacklogDto>(
            $"api/repositories/{repoId}/backlog", JsonOptions);

        Assert.NotNull(backlog);
        Assert.NotEmpty(backlog.Epics);
        Assert.Equal("Test Epic", backlog.Epics[0].Title);
    }

    [Fact]
    public async Task Backlog_AddEpic_CreatesEpic()
    {
        var repoId = await SeedRepoAsync("cli-epic-test");

        var response = await _client.PostAsJsonAsync(
            $"api/repositories/{repoId}/epics",
            new CreateEpicRequest("CLI Epic", "Created via CLI", null, null), JsonOptions);

        response.EnsureSuccessStatusCode();
        var epic = await response.Content.ReadFromJsonAsync<EpicDto>(JsonOptions);
        Assert.NotNull(epic);
        Assert.Equal("CLI Epic", epic.Title);
    }

    [Fact]
    public async Task Backlog_AddFeatureAndStory_CreatesHierarchy()
    {
        var repoId = await SeedRepoAsync("cli-hierarchy-test");

        var epicResp = await _client.PostAsJsonAsync(
            $"api/repositories/{repoId}/epics",
            new CreateEpicRequest("E", null, null, null), JsonOptions);
        var epic = await epicResp.Content.ReadFromJsonAsync<EpicDto>(JsonOptions);

        var featureResp = await _client.PostAsJsonAsync(
            $"api/epics/{epic!.Id}/features",
            new CreateFeatureRequest("F", null, null, null), JsonOptions);
        var feature = await featureResp.Content.ReadFromJsonAsync<FeatureDto>(JsonOptions);

        var storyResp = await _client.PostAsJsonAsync(
            $"api/features/{feature!.Id}/stories",
            new CreateUserStoryRequest("CLI Story", null, null, 3, null, null), JsonOptions);
        var story = await storyResp.Content.ReadFromJsonAsync<UserStoryDto>(JsonOptions);

        Assert.NotNull(story);
        Assert.Equal("CLI Story", story.Title);
        Assert.Equal(3, story.StoryPoints);
    }

    [Fact]
    public async Task Backlog_SetStatus_UpdatesStory()
    {
        var repoId = await SeedRepoAsync("cli-status-test");
        var epic = await CreateAsync<EpicDto>($"api/repositories/{repoId}/epics",
            new CreateEpicRequest("E", null, null, null));
        var feature = await CreateAsync<FeatureDto>($"api/epics/{epic.Id}/features",
            new CreateFeatureRequest("F", null, null, null));
        var story = await CreateAsync<UserStoryDto>($"api/features/{feature.Id}/stories",
            new CreateUserStoryRequest("S", null, null, null, null, null));

        var patchContent = JsonContent.Create(
            new UpdateUserStoryStatusRequest("Ready", null), options: JsonOptions);
        var resp = await _client.PatchAsync($"api/stories/{story.Id}/status", patchContent);
        resp.EnsureSuccessStatusCode();
        var updated = await resp.Content.ReadFromJsonAsync<UserStoryDto>(JsonOptions);

        Assert.NotNull(updated);
        Assert.Equal("Ready", updated.Status);
    }

    // ── Sandboxes ───────────────────────────────────────────────────

    [Fact]
    public async Task Sandbox_List_ReturnsEmptyList()
    {
        var list = await _client.GetFromJsonAsync<List<SandboxDto>>("api/sandboxes", JsonOptions);

        Assert.NotNull(list);
    }

    // ── MCP Configs ─────────────────────────────────────────────────

    [Fact]
    public async Task Mcp_CreateAndToggle_Works()
    {
        var createResp = await _client.PostAsJsonAsync("api/mcp",
            new CreateMcpServerConfigRequest("cli-test-mcp", null, "stdio", "/bin/echo", null, null, null, null, null),
            JsonOptions);
        createResp.EnsureSuccessStatusCode();
        var config = await createResp.Content.ReadFromJsonAsync<McpServerConfigDto>(JsonOptions);
        Assert.NotNull(config);
        Assert.True(config.Enabled);

        var toggleResp = await _client.PostAsJsonAsync($"api/mcp/{config.Id}/toggle", new { }, JsonOptions);
        toggleResp.EnsureSuccessStatusCode();
        var toggled = await toggleResp.Content.ReadFromJsonAsync<McpServerConfigDto>(JsonOptions);
        Assert.NotNull(toggled);
        Assert.False(toggled.Enabled);
    }

    // ── Artifact Feeds ──────────────────────────────────────────────

    [Fact]
    public async Task ArtifactFeeds_ListEnabled_Returns()
    {
        var list = await _client.GetFromJsonAsync<List<ArtifactFeedConfigDto>>(
            "api/artifact/enabled", JsonOptions);

        Assert.NotNull(list);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task<T> CreateAsync<T>(string path, object body)
    {
        var resp = await _client.PostAsJsonAsync(path, body, JsonOptions);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task<Guid> SeedRepoAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "dev-user",
            Name = name,
            CloneUrl = $"https://github.com/test/{name}.git"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        return repo.Id;
    }

    private async Task SeedUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<UserDirectoryEntry>().Add(new UserDirectoryEntry
        {
            Id = Guid.NewGuid(),
            UserId = $"user-{Guid.NewGuid():N}",
            Email = email,
            DisplayName = email
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedRepoWithBacklogAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "dev-user",
            Name = name,
            CloneUrl = $"https://github.com/test/{name}.git"
        };
        db.Repositories.Add(repo);

        var epic = new Epic
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Title = "Test Epic",
            Order = 0
        };
        db.Set<Epic>().Add(epic);

        var feature = new Feature
        {
            Id = Guid.NewGuid(),
            EpicId = epic.Id,
            Title = "Test Feature",
            Order = 0
        };
        db.Set<Feature>().Add(feature);

        var story = new UserStory
        {
            Id = Guid.NewGuid(),
            FeatureId = feature.Id,
            Title = "Test Story",
            Order = 0
        };
        db.Set<UserStory>().Add(story);

        await db.SaveChangesAsync();
        return repo.Id;
    }
}
