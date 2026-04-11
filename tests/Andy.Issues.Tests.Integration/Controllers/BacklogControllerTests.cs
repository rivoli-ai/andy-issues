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
