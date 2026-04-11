// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class BacklogAzureDevOpsSyncTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BacklogAzureDevOpsSyncTests()
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

    private BacklogAzureDevOpsSyncService NewSync(AppDbContext ctx, StubAzureDevOpsClient client) =>
        new(ctx, client, new RepositoryAccessGuard(ctx),
            NullLogger<BacklogAzureDevOpsSyncService>.Instance);

    private async Task<(Guid repoId, Guid storyId)> SeedAsync(string cloneUrl, RepositoryProvider provider = RepositoryProvider.AzureDevOps)
    {
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "r",
            Provider = provider,
            CloneUrl = cloneUrl
        };
        ctx.Repositories.Add(repo);

        ctx.LinkedProviders.Add(new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Provider = LinkedProviderKind.AzureDevOps,
            AccessToken = "pat"
        });

        var epic = new Epic { Id = Guid.NewGuid(), RepositoryId = repo.Id, Title = "E", Order = 1 };
        ctx.Epics.Add(epic);
        var feature = new Feature { Id = Guid.NewGuid(), EpicId = epic.Id, Title = "F", Order = 1 };
        ctx.Features.Add(feature);
        var story = new UserStory
        {
            Id = Guid.NewGuid(),
            FeatureId = feature.Id,
            Title = "S1",
            Description = "story desc",
            Order = 1
        };
        ctx.UserStories.Add(story);
        await ctx.SaveChangesAsync();
        return (repo.Id, story.Id);
    }

    [Theory]
    [InlineData(UserStoryStatus.Draft, "New")]
    [InlineData(UserStoryStatus.Ready, "Active")]
    [InlineData(UserStoryStatus.InProgress, "Active")]
    [InlineData(UserStoryStatus.InReview, "Resolved")]
    [InlineData(UserStoryStatus.Done, "Closed")]
    public void Mapping_StatusToAzureState(UserStoryStatus local, string remote)
    {
        Assert.Equal(remote, AzureDevOpsWorkItemMapping.ToAzureState(local));
    }

    [Theory]
    [InlineData("Closed", true)]
    [InlineData("Done", true)]
    [InlineData("Removed", true)]
    [InlineData("Active", false)]
    [InlineData("Resolved", false)]
    [InlineData("New", false)]
    public void Mapping_IsDoneState(string state, bool expected)
    {
        Assert.Equal(expected, AzureDevOpsWorkItemMapping.IsDoneState(state));
    }

    [Fact]
    public void TryParseOrgProject_DevAzureCom()
    {
        Assert.True(BacklogAzureDevOpsSyncService.TryParseOrgProject(
            "https://dev.azure.com/myorg/myproject/_git/myrepo", out var org, out var project));
        Assert.Equal("myorg", org);
        Assert.Equal("myproject", project);
    }

    [Fact]
    public void TryParseOrgProject_VisualStudioCom()
    {
        Assert.True(BacklogAzureDevOpsSyncService.TryParseOrgProject(
            "https://myorg.visualstudio.com/myproject/_git/myrepo", out var org, out var project));
        Assert.Equal("myorg", org);
        Assert.Equal("myproject", project);
    }

    [Fact]
    public void TryParseOrgProject_UnknownUrl_Fails()
    {
        Assert.False(BacklogAzureDevOpsSyncService.TryParseOrgProject(
            "https://github.com/foo/bar.git", out _, out _));
    }

    [Fact]
    public async Task Push_NewStory_CreatesWorkItemAndPersistsId()
    {
        var (repoId, storyId) = await SeedAsync("https://dev.azure.com/org/proj/_git/repo");
        var client = new StubAzureDevOpsClient();

        await using (var ctx = NewContext())
        {
            var result = await NewSync(ctx, client).PushAsync(repoId, "alice");
            Assert.NotNull(result);
            Assert.Equal(1, result!.Added);
            Assert.Single(client.UpsertCalls);
            Assert.Null(client.UpsertCalls[0].ExistingId);
            Assert.Equal("S1", client.UpsertCalls[0].Title);
            Assert.Equal("New", client.UpsertCalls[0].State);
        }

        await using (var ctx = NewContext())
        {
            var story = await ctx.UserStories.FindAsync(storyId);
            Assert.NotNull(story!.AzureDevOpsWorkItemId);
        }
    }

    [Fact]
    public async Task Push_ExistingWorkItem_UpdatesWithoutCreating()
    {
        var (repoId, storyId) = await SeedAsync("https://dev.azure.com/org/proj/_git/repo");
        await using (var ctx = NewContext())
        {
            var story = await ctx.UserStories.FindAsync(storyId);
            story!.AzureDevOpsWorkItemId = 777;
            await ctx.SaveChangesAsync();
        }

        var client = new StubAzureDevOpsClient().SeedWorkItem(777, "S1", "Active");
        await using var ctx2 = NewContext();
        var result = await NewSync(ctx2, client).PushAsync(repoId, "alice");
        Assert.NotNull(result);
        Assert.Equal(0, result!.Added);
        Assert.Equal(1, result.Updated);
        Assert.Equal(777, client.UpsertCalls[0].ExistingId);
    }

    [Fact]
    public async Task Push_NonAzureRepo_ReturnsError()
    {
        var (repoId, _) = await SeedAsync(
            "https://github.com/org/repo.git",
            provider: RepositoryProvider.GitHub);
        var client = new StubAzureDevOpsClient();
        await using var ctx = NewContext();
        var result = await NewSync(ctx, client).PushAsync(repoId, "alice");
        Assert.NotNull(result);
        Assert.NotEmpty(result!.Errors);
    }

    [Fact]
    public async Task Push_NoLinkedProvider_ReturnsError()
    {
        // Seed a repo without a linked provider for a different user.
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "r",
            Provider = RepositoryProvider.AzureDevOps,
            CloneUrl = "https://dev.azure.com/org/proj/_git/repo"
        };
        ctx.Repositories.Add(repo);
        await ctx.SaveChangesAsync();

        var client = new StubAzureDevOpsClient();
        var result = await NewSync(ctx, client).PushAsync(repo.Id, "alice");
        Assert.NotNull(result);
        Assert.Contains(result!.Errors, e => e.Contains("linked provider", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Push_Stranger_ReturnsNull()
    {
        var (repoId, _) = await SeedAsync("https://dev.azure.com/org/proj/_git/repo");
        var client = new StubAzureDevOpsClient();
        await using var ctx = NewContext();
        var result = await NewSync(ctx, client).PushAsync(repoId, "mallory");
        Assert.Null(result);
    }

    [Fact]
    public async Task Pull_RemoteClosed_ForcesLocalDone()
    {
        var (repoId, storyId) = await SeedAsync("https://dev.azure.com/org/proj/_git/repo");
        await using (var ctx = NewContext())
        {
            var story = await ctx.UserStories.FindAsync(storyId);
            story!.AzureDevOpsWorkItemId = 101;
            await ctx.SaveChangesAsync();
        }

        var client = new StubAzureDevOpsClient().SeedWorkItem(101, "S1", "Closed");
        await using (var ctx = NewContext())
        {
            var result = await NewSync(ctx, client).PullAsync(repoId, "alice");
            Assert.NotNull(result);
            Assert.Equal(1, result!.Updated);
        }

        await using (var ctx = NewContext())
        {
            var story = await ctx.UserStories.FindAsync(storyId);
            Assert.Equal(UserStoryStatus.Done, story!.Status);
        }
    }

    [Fact]
    public async Task Pull_RemoteNonDoneState_LeavesLocalAlone()
    {
        var (repoId, storyId) = await SeedAsync("https://dev.azure.com/org/proj/_git/repo");
        await using (var ctx = NewContext())
        {
            var story = await ctx.UserStories.FindAsync(storyId);
            story!.AzureDevOpsWorkItemId = 102;
            story.SetStatus(UserStoryStatus.InProgress);
            await ctx.SaveChangesAsync();
        }

        var client = new StubAzureDevOpsClient().SeedWorkItem(102, "Different", "Active");
        await using (var ctx = NewContext())
        {
            var result = await NewSync(ctx, client).PullAsync(repoId, "alice");
            Assert.Equal(0, result!.Updated);
            Assert.Equal(1, result.Skipped);
        }

        await using (var ctx = NewContext())
        {
            var story = await ctx.UserStories.FindAsync(storyId);
            Assert.Equal(UserStoryStatus.InProgress, story!.Status);
            Assert.Equal("S1", story.Title); // local-authoritative: not overwritten
        }
    }

    [Fact]
    public async Task Pull_NoLinkedStories_ReturnsZeroes()
    {
        var (repoId, _) = await SeedAsync("https://dev.azure.com/org/proj/_git/repo");
        var client = new StubAzureDevOpsClient();
        await using var ctx = NewContext();
        var result = await NewSync(ctx, client).PullAsync(repoId, "alice");
        Assert.NotNull(result);
        Assert.Equal(0, result!.Updated);
    }
}
