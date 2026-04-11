// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class ArtifactFeedServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ArtifactFeedServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new AppDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext NewContext() => new(_options);
    private ArtifactFeedService NewService(AppDbContext ctx, IAzureDevOpsClient? azure = null) =>
        new(ctx, azure);

    [Fact]
    public async Task Create_Valid_Succeeds()
    {
        await using var ctx = NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateArtifactFeedConfigRequest("pkgs", "rivoli", "pkgs", "shared", "Nuget"));
        Assert.Equal(ArtifactFeedOutcome.Ok, result.Outcome);
        Assert.NotNull(result.Dto);
        Assert.True(result.Dto!.Enabled);
    }

    [Fact]
    public async Task Create_BadType_ReturnsInvalid()
    {
        await using var ctx = NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateArtifactFeedConfigRequest("pkgs", "rivoli", "pkgs", null, "Cargo"));
        Assert.Equal(ArtifactFeedOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        await using var ctx = NewContext();
        var svc = NewService(ctx);
        await svc.CreateAsync(new CreateArtifactFeedConfigRequest("feed", "rivoli", "p", null, "Nuget"));
        var again = await svc.CreateAsync(new CreateArtifactFeedConfigRequest("feed", "rivoli", "q", null, "Nuget"));
        Assert.Equal(ArtifactFeedOutcome.Conflict, again.Outcome);
    }

    [Fact]
    public async Task Update_TogglesEnabled()
    {
        Guid id;
        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).CreateAsync(
                new CreateArtifactFeedConfigRequest("feed", "rivoli", "p", null, "Nuget"));
            id = result.Dto!.Id;
        }

        await using var ctx2 = NewContext();
        var updated = await NewService(ctx2).UpdateAsync(
            id,
            new UpdateArtifactFeedConfigRequest(Name: null, Project: null, Enabled: false));
        Assert.Equal(ArtifactFeedOutcome.Ok, updated.Outcome);
        Assert.False(updated.Dto!.Enabled);
    }

    [Fact]
    public async Task Update_Missing_ReturnsNotFound()
    {
        await using var ctx = NewContext();
        var result = await NewService(ctx).UpdateAsync(
            Guid.NewGuid(),
            new UpdateArtifactFeedConfigRequest(null, null, null));
        Assert.Equal(ArtifactFeedOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task GetEnabledAsync_FiltersDisabled()
    {
        await using (var ctx = NewContext())
        {
            ctx.ArtifactFeedConfigs.AddRange(
                new ArtifactFeedConfig
                {
                    Id = Guid.NewGuid(),
                    Name = "on",
                    Organization = "rivoli",
                    FeedName = "p",
                    Type = ArtifactFeedType.Nuget,
                    Enabled = true
                },
                new ArtifactFeedConfig
                {
                    Id = Guid.NewGuid(),
                    Name = "off",
                    Organization = "rivoli",
                    FeedName = "p2",
                    Type = ArtifactFeedType.Nuget,
                    Enabled = false
                });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        var enabled = await NewService(ctx2).GetEnabledAsync();
        Assert.Single(enabled);
        Assert.Equal("on", enabled[0].Name);
    }

    [Fact]
    public async Task Delete_Missing_ReturnsNotFound()
    {
        await using var ctx = NewContext();
        var outcome = await NewService(ctx).DeleteAsync(Guid.NewGuid());
        Assert.Equal(ArtifactFeedOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task BrowseAzureDevOpsFeeds_NoLinkedProvider_ReturnsTypedError()
    {
        var azure = new StubAzureDevOpsClient();
        await using var ctx = NewContext();
        var result = await NewService(ctx, azure).BrowseAzureDevOpsFeedsAsync("alice", "rivoli");
        Assert.Equal(ArtifactFeedBrowseOutcome.NoLinkedProvider, result.Outcome);
    }

    [Fact]
    public async Task BrowseAzureDevOpsFeeds_Succeeds()
    {
        await using (var ctx = NewContext())
        {
            ctx.LinkedProviders.Add(new LinkedProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "alice",
                Provider = LinkedProviderKind.AzureDevOps,
                AccessToken = "pat"
            });
            await ctx.SaveChangesAsync();
        }

        var azure = new StubAzureDevOpsClient();
        azure.FeedResponses["rivoli"] = new[]
        {
            new AzureDevOpsFeedInfo("feed-1", "primary", "main feed", "https://feeds.dev.azure.com/rivoli/feed-1")
        };

        await using var ctx2 = NewContext();
        var result = await NewService(ctx2, azure).BrowseAzureDevOpsFeedsAsync("alice", "rivoli");
        Assert.Equal(ArtifactFeedBrowseOutcome.Ok, result.Outcome);
        Assert.Single(result.Feeds!);
        Assert.Equal("primary", result.Feeds![0].Name);
    }

    [Fact]
    public async Task BrowseAzureDevOpsFeeds_EmptyOrg_ReturnsProviderError()
    {
        var azure = new StubAzureDevOpsClient();
        await using var ctx = NewContext();
        var result = await NewService(ctx, azure).BrowseAzureDevOpsFeedsAsync("alice", "");
        Assert.Equal(ArtifactFeedBrowseOutcome.ProviderError, result.Outcome);
    }
}
