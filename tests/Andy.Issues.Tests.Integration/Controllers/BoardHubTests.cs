// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

public class BoardHubTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BoardHubTests(TestWebApplicationFactory factory)
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
            Name = $"hub-{Guid.NewGuid():N}",
            CloneUrl = "https://example.com/hub.git"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        return repo.Id;
    }

    private HubConnection BuildConnection()
    {
        return new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/board", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }

    [Fact]
    public async Task StoryMutation_BroadcastsToJoinedClients()
    {
        var repoId = await SeedRepoAsync();

        // Seed an epic + feature so we can create a story.
        var epicResp = await _client.PostAsJsonAsync(
            $"/api/repositories/{repoId}/epics",
            new CreateEpicRequest("E", null, null, null));
        var epic = await epicResp.Content.ReadFromJsonAsync<EpicDto>(JsonOptions);
        var featureResp = await _client.PostAsJsonAsync(
            $"/api/epics/{epic!.Id}/features",
            new CreateFeatureRequest("F", null, null, null));
        var feature = await featureResp.Content.ReadFromJsonAsync<FeatureDto>(JsonOptions);

        await using var connection = BuildConnection();

        var added = new TaskCompletionSource<UserStoryDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<UserStoryDto>("StoryAdded", dto => added.TrySetResult(dto));

        await connection.StartAsync();
        await connection.InvokeAsync("JoinRepository", repoId);

        var storyResp = await _client.PostAsJsonAsync(
            $"/api/features/{feature!.Id}/stories",
            new CreateUserStoryRequest("S-live", null, null, null, null, null));
        storyResp.EnsureSuccessStatusCode();

        var completed = await Task.WhenAny(added.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(completed == added.Task, "SignalR StoryAdded event was not received in time.");
        var received = await added.Task;
        Assert.Equal("S-live", received.Title);
    }

    [Fact]
    public async Task JoinRepository_UnauthorizedRepo_Throws()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "someone-else",
            Name = "stranger-hub",
            CloneUrl = "https://example.com/s.git"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        await using var connection = BuildConnection();
        await connection.StartAsync();

        var ex = await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync("JoinRepository", repo.Id));
        Assert.Contains("Access denied", ex.Message);
    }
}
