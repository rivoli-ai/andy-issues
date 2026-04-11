// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

public class ArtifactControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ArtifactControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.ResetPermissions();
        _factory.FakeAzureDevOpsClient.Reset();
    }

    [Fact]
    public async Task GetEnabled_NonAdmin_IsAccessible()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ArtifactFeedConfigs.Add(new ArtifactFeedConfig
            {
                Id = Guid.NewGuid(),
                Name = $"pub-{Guid.NewGuid():N}",
                Organization = "rivoli",
                FeedName = "p",
                Type = ArtifactFeedType.Nuget,
                Enabled = true
            });
            await db.SaveChangesAsync();
        }

        _factory.PermissionCheckerFake.SetAdmin(false);
        var resp = await _client.GetAsync("/api/artifact/enabled");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var list = await resp.Content.ReadFromJsonAsync<List<ArtifactFeedConfigDto>>(JsonOptions);
        Assert.NotEmpty(list!);
    }

    [Fact]
    public async Task List_NonAdmin_Returns403()
    {
        _factory.PermissionCheckerFake.SetAdmin(false);
        var resp = await _client.GetAsync("/api/artifact");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task List_Admin_ReturnsAll()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ArtifactFeedConfigs.Add(new ArtifactFeedConfig
            {
                Id = Guid.NewGuid(),
                Name = $"admin-list-{Guid.NewGuid():N}",
                Organization = "rivoli",
                FeedName = "p",
                Type = ArtifactFeedType.Npm,
                Enabled = false
            });
            await db.SaveChangesAsync();
        }

        _factory.PermissionCheckerFake.SetAdmin(true);
        var resp = await _client.GetAsync("/api/artifact");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var list = await resp.Content.ReadFromJsonAsync<List<ArtifactFeedConfigDto>>(JsonOptions);
        Assert.Contains(list!, f => !f.Enabled); // includes disabled rows
    }

    [Fact]
    public async Task Create_Admin_PersistsAndReturns201()
    {
        _factory.PermissionCheckerFake.SetAdmin(true);
        var name = $"new-{Guid.NewGuid():N}";
        var resp = await _client.PostAsJsonAsync("/api/artifact",
            new CreateArtifactFeedConfigRequest(name, "rivoli", "pkgs", null, "Pip"));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<ArtifactFeedConfigDto>(JsonOptions);
        Assert.Equal(name, dto!.Name);
        Assert.True(dto.Enabled);
    }

    [Fact]
    public async Task Create_NonAdmin_Returns403()
    {
        _factory.PermissionCheckerFake.SetAdmin(false);
        var resp = await _client.PostAsJsonAsync("/api/artifact",
            new CreateArtifactFeedConfigRequest("x", "rivoli", "p", null, "Nuget"));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Patch_Admin_UpdatesEnabledFlag()
    {
        _factory.PermissionCheckerFake.SetAdmin(true);

        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = new ArtifactFeedConfig
            {
                Id = Guid.NewGuid(),
                Name = $"patch-{Guid.NewGuid():N}",
                Organization = "rivoli",
                FeedName = "p",
                Type = ArtifactFeedType.Nuget,
                Enabled = true
            };
            db.ArtifactFeedConfigs.Add(entity);
            await db.SaveChangesAsync();
            id = entity.Id;
        }

        var patch = new UpdateArtifactFeedConfigRequest(Name: null, Project: null, Enabled: false);
        var resp = await _client.PatchAsJsonAsync($"/api/artifact/{id}", patch);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<ArtifactFeedConfigDto>(JsonOptions);
        Assert.False(dto!.Enabled);
    }

    [Fact]
    public async Task Delete_Admin_Returns204()
    {
        _factory.PermissionCheckerFake.SetAdmin(true);

        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = new ArtifactFeedConfig
            {
                Id = Guid.NewGuid(),
                Name = $"del-{Guid.NewGuid():N}",
                Organization = "rivoli",
                FeedName = "p",
                Type = ArtifactFeedType.Nuget,
                Enabled = true
            };
            db.ArtifactFeedConfigs.Add(entity);
            await db.SaveChangesAsync();
            id = entity.Id;
        }

        var resp = await _client.DeleteAsync($"/api/artifact/{id}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    [Fact]
    public async Task BrowseFeeds_Admin_WithLinkedProvider_Returns200()
    {
        _factory.PermissionCheckerFake.SetAdmin(true);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LinkedProviders.Add(new LinkedProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "dev-user",
                Provider = LinkedProviderKind.AzureDevOps,
                AccessToken = "pat"
            });
            await db.SaveChangesAsync();
        }

        _factory.FakeAzureDevOpsClient.FeedResponses["rivoli"] = new[]
        {
            new AzureDevOpsFeedInfo("feed-1", "primary", "main feed", "https://feeds.dev.azure.com/rivoli/feed-1")
        };

        var resp = await _client.GetAsync("/api/artifact/feeds?organization=rivoli");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var feeds = doc.RootElement.GetProperty("feeds").EnumerateArray().ToList();
        Assert.Single(feeds);
        Assert.Equal("primary", feeds[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task BrowseFeeds_Admin_NoLinkedProvider_Returns400()
    {
        _factory.PermissionCheckerFake.SetAdmin(true);
        var resp = await _client.GetAsync("/api/artifact/feeds?organization=rivoli");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task BrowseFeeds_NonAdmin_Returns403()
    {
        _factory.PermissionCheckerFake.SetAdmin(false);
        var resp = await _client.GetAsync("/api/artifact/feeds?organization=rivoli");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
