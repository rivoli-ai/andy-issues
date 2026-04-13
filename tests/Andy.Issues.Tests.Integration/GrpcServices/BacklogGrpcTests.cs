// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Protos;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.GrpcServices;

public class BacklogGrpcTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly TestWebApplicationFactory _factory;
    private readonly GrpcChannel _channel;
    private readonly BacklogService.BacklogServiceClient _client;

    public BacklogGrpcTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        var httpClient = factory.CreateDefaultClient();
        _channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });
        _client = new BacklogService.BacklogServiceClient(_channel);
    }

    public void Dispose()
    {
        _channel.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetBacklog_ReturnsEmptyForNewRepo()
    {
        var repoId = await SeedRepoAsync();

        var response = await _client.GetBacklogAsync(new GetBacklogRequest
        {
            RepositoryId = repoId.ToString()
        });

        Assert.Equal(repoId.ToString(), response.RepositoryId);
        Assert.Empty(response.Epics);
    }

    [Fact]
    public async Task AddEpic_CreatesAndReturnsEpic()
    {
        var repoId = await SeedRepoAsync();

        var epic = await _client.AddEpicAsync(new AddEpicRequest
        {
            RepositoryId = repoId.ToString(),
            Title = "gRPC Epic"
        });

        Assert.Equal("gRPC Epic", epic.Title);
        Assert.NotEmpty(epic.Id);
    }

    [Fact]
    public async Task AddFeature_ThenAddStory_BuildsHierarchy()
    {
        var repoId = await SeedRepoAsync();

        var epic = await _client.AddEpicAsync(new AddEpicRequest
        {
            RepositoryId = repoId.ToString(),
            Title = "E1"
        });

        var feature = await _client.AddFeatureAsync(new AddFeatureRequest
        {
            EpicId = epic.Id,
            Title = "F1"
        });

        var story = await _client.AddStoryAsync(new AddStoryRequest
        {
            FeatureId = feature.Id,
            Title = "S1",
            Description = "As a user, I want gRPC"
        });

        Assert.Equal("S1", story.Title);
        Assert.Equal("Draft", story.Status);
    }

    [Fact]
    public async Task UpdateStoryStatus_Works()
    {
        var repoId = await SeedRepoAsync();
        var epic = await _client.AddEpicAsync(new AddEpicRequest
        {
            RepositoryId = repoId.ToString(),
            Title = "E"
        });
        var feature = await _client.AddFeatureAsync(new AddFeatureRequest
        {
            EpicId = epic.Id,
            Title = "F"
        });
        var story = await _client.AddStoryAsync(new AddStoryRequest
        {
            FeatureId = feature.Id,
            Title = "S"
        });

        var result = await _client.UpdateStoryStatusAsync(new Andy.Issues.Api.Protos.UpdateStoryStatusRequest
        {
            StoryId = story.Id,
            Status = "Ready"
        });

        Assert.Equal("updated", result.Outcome);
        Assert.Equal("Ready", result.Story.Status);
    }

    [Fact]
    public async Task DeleteEpic_ReturnsTrue()
    {
        var repoId = await SeedRepoAsync();
        var epic = await _client.AddEpicAsync(new AddEpicRequest
        {
            RepositoryId = repoId.ToString(),
            Title = "ToDelete"
        });

        var result = await _client.DeleteEpicAsync(new DeleteEpicRequest { EpicId = epic.Id });

        Assert.True(result.Deleted);
    }

    private async Task<Guid> SeedRepoAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "dev-user",
            Name = $"backlog-grpc-{Guid.NewGuid():N}",
            CloneUrl = $"https://github.com/test/{Guid.NewGuid()}.git"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        return repo.Id;
    }
}
