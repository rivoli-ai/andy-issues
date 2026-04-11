// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

public class BacklogControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BacklogControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<Guid> SeedRepoAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "dev-user",
            Name = $"backlog-{Guid.NewGuid():N}",
            CloneUrl = "https://example.com/b.git"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        return repo.Id;
    }

    [Fact]
    public async Task FullCrud_HappyPath()
    {
        var repoId = await SeedRepoAsync();

        // 1. Empty backlog on a fresh repo.
        var getResp = await _client.GetAsync($"/api/repositories/{repoId}/backlog");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var empty = await getResp.Content.ReadFromJsonAsync<BacklogDto>(JsonOptions);
        Assert.Empty(empty!.Epics);

        // 2. Create an epic.
        var epicResp = await _client.PostAsJsonAsync(
            $"/api/repositories/{repoId}/epics",
            new CreateEpicRequest("Epic A", "epic desc", null, null));
        Assert.Equal(HttpStatusCode.Created, epicResp.StatusCode);
        var epic = await epicResp.Content.ReadFromJsonAsync<EpicDto>(JsonOptions);
        Assert.NotNull(epic);

        // 3. Create a feature under it.
        var featureResp = await _client.PostAsJsonAsync(
            $"/api/epics/{epic!.Id}/features",
            new CreateFeatureRequest("Feature A", null, null, null));
        Assert.Equal(HttpStatusCode.OK, featureResp.StatusCode);
        var feature = await featureResp.Content.ReadFromJsonAsync<FeatureDto>(JsonOptions);

        // 4. Create a story under it.
        var storyResp = await _client.PostAsJsonAsync(
            $"/api/features/{feature!.Id}/stories",
            new CreateUserStoryRequest("Story A", null, "AC", 3, null, null));
        Assert.Equal(HttpStatusCode.OK, storyResp.StatusCode);

        // 5. Get the full tree.
        var fullResp = await _client.GetAsync($"/api/repositories/{repoId}/backlog");
        var full = await fullResp.Content.ReadFromJsonAsync<BacklogDto>(JsonOptions);
        Assert.Single(full!.Epics);
        Assert.Single(full.Epics[0].Features);
        Assert.Single(full.Epics[0].Features[0].Stories);

        // 6. Delete the epic — cascades everything.
        var delResp = await _client.DeleteAsync($"/api/epics/{epic.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        var afterResp = await _client.GetAsync($"/api/repositories/{repoId}/backlog");
        var after = await afterResp.Content.ReadFromJsonAsync<BacklogDto>(JsonOptions);
        Assert.Empty(after!.Epics);
    }

    [Fact]
    public async Task UpdateStoryStatus_EndToEnd()
    {
        var repoId = await SeedRepoAsync();
        var epicResp = await _client.PostAsJsonAsync(
            $"/api/repositories/{repoId}/epics",
            new CreateEpicRequest("E", null, null, null));
        var epic = await epicResp.Content.ReadFromJsonAsync<EpicDto>(JsonOptions);
        var featureResp = await _client.PostAsJsonAsync(
            $"/api/epics/{epic!.Id}/features",
            new CreateFeatureRequest("F", null, null, null));
        var feature = await featureResp.Content.ReadFromJsonAsync<FeatureDto>(JsonOptions);
        var storyResp = await _client.PostAsJsonAsync(
            $"/api/features/{feature!.Id}/stories",
            new CreateUserStoryRequest("S", null, null, null, null, null));
        var story = await storyResp.Content.ReadFromJsonAsync<UserStoryDto>(JsonOptions);

        // Happy path: transition to InReview with a PR URL.
        var patchResp = await _client.PatchAsJsonAsync(
            $"/api/stories/{story!.Id}/status",
            new UpdateUserStoryStatusRequest("InReview", "https://example.com/pr/42"));
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var updated = await patchResp.Content.ReadFromJsonAsync<UserStoryDto>(JsonOptions);
        Assert.Equal("InReview", updated!.Status);
        Assert.Equal("https://example.com/pr/42", updated.PullRequestUrl);

        // Move to Done.
        await _client.PatchAsJsonAsync(
            $"/api/stories/{story.Id}/status",
            new UpdateUserStoryStatusRequest("Done", null));

        // Done → Draft is blocked with 409.
        var rejectResp = await _client.PatchAsJsonAsync(
            $"/api/stories/{story.Id}/status",
            new UpdateUserStoryStatusRequest("Draft", null));
        Assert.Equal(HttpStatusCode.Conflict, rejectResp.StatusCode);

        // Unknown status string → 400.
        var badResp = await _client.PatchAsJsonAsync(
            $"/api/stories/{story.Id}/status",
            new UpdateUserStoryStatusRequest("Bogus", null));
        Assert.Equal(HttpStatusCode.BadRequest, badResp.StatusCode);
    }

    [Fact]
    public async Task SyncAzureDevOps_PushesWorkItemsAndPersistsIds()
    {
        Guid repoId;
        Guid storyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var repo = new Repository
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "dev-user",
                Name = "azdo-repo",
                Provider = RepositoryProvider.AzureDevOps,
                CloneUrl = "https://dev.azure.com/myorg/myproject/_git/myrepo"
            };
            db.Repositories.Add(repo);
            db.LinkedProviders.Add(new LinkedProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "dev-user",
                Provider = LinkedProviderKind.AzureDevOps,
                AccessToken = "pat-token"
            });
            var epic = new Epic { Id = Guid.NewGuid(), RepositoryId = repo.Id, Title = "E", Order = 1 };
            db.Epics.Add(epic);
            var feature = new Feature { Id = Guid.NewGuid(), EpicId = epic.Id, Title = "F", Order = 1 };
            db.Features.Add(feature);
            var story = new UserStory
            {
                Id = Guid.NewGuid(),
                FeatureId = feature.Id,
                Title = "sync-me",
                Order = 1
            };
            db.UserStories.Add(story);
            await db.SaveChangesAsync();
            repoId = repo.Id;
            storyId = story.Id;
        }

        _factory.FakeAzureDevOpsClient.Reset();

        var resp = await _client.PostAsync($"/api/repositories/{repoId}/sync-azure-devops", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = await resp.Content.ReadFromJsonAsync<SyncResult>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(1, result!.Added);

        Assert.Single(_factory.FakeAzureDevOpsClient.WorkItems);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db2.UserStories.FindAsync(storyId);
        Assert.NotNull(persisted!.AzureDevOpsWorkItemId);
    }

    [Fact]
    public async Task GetBacklog_StrangerRepo_Returns404()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "someone-else",
            Name = "stranger",
            CloneUrl = "https://example.com/s.git"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        var resp = await _client.GetAsync($"/api/repositories/{repo.Id}/backlog");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
