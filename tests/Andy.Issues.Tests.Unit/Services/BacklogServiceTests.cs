// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class BacklogServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BacklogServiceTests()
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
    private BacklogService NewService(AppDbContext ctx) =>
        new(ctx, new RepositoryAccessGuard(ctx));

    private async Task<Guid> SeedRepoAsync(string owner = "alice", bool shareWith = false)
    {
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner,
            Name = "repo",
            CloneUrl = "https://example.com/r.git"
        };
        if (shareWith)
            repo.AddShare("bob", owner);
        ctx.Repositories.Add(repo);
        await ctx.SaveChangesAsync();
        return repo.Id;
    }

    [Fact]
    public async Task AddEpic_Owner_Succeeds()
    {
        var repoId = await SeedRepoAsync();
        await using var ctx = NewContext();
        var dto = await NewService(ctx).AddEpicAsync(repoId, new CreateEpicRequest("Title", "desc", null, null), "alice");
        Assert.NotNull(dto);
        Assert.Equal(1, dto!.Order);
    }

    [Fact]
    public async Task AddEpic_SharedUserSucceeds()
    {
        var repoId = await SeedRepoAsync("alice", shareWith: true);
        await using var ctx = NewContext();
        var dto = await NewService(ctx).AddEpicAsync(repoId, new CreateEpicRequest("T", null, null, null), "bob");
        Assert.NotNull(dto);
    }

    [Fact]
    public async Task AddEpic_Stranger_Rejected()
    {
        var repoId = await SeedRepoAsync();
        await using var ctx = NewContext();
        var dto = await NewService(ctx).AddEpicAsync(repoId, new CreateEpicRequest("T", null, null, null), "mallory");
        Assert.Null(dto);
    }

    [Fact]
    public async Task GetBacklog_ReturnsOrderedHierarchy()
    {
        var repoId = await SeedRepoAsync();
        await using (var ctx = NewContext())
        {
            var svc = NewService(ctx);
            var e1 = await svc.AddEpicAsync(repoId, new CreateEpicRequest("E1", null, null, null), "alice");
            var e2 = await svc.AddEpicAsync(repoId, new CreateEpicRequest("E2", null, null, null), "alice");
            var f1 = await svc.AddFeatureAsync(e1!.Id, new CreateFeatureRequest("F1", null, null, null), "alice");
            await svc.AddStoryAsync(f1!.Id, new CreateUserStoryRequest("S1", null, null, null, null, null), "alice");
            await svc.AddStoryAsync(f1.Id, new CreateUserStoryRequest("S2", null, null, null, null, null), "alice");
        }

        await using var ctx2 = NewContext();
        var dto = await NewService(ctx2).GetAsync(repoId, "alice");
        Assert.NotNull(dto);
        Assert.Equal(2, dto!.Epics.Count);
        Assert.Equal("E1", dto.Epics[0].Title);
        var feature = Assert.Single(dto.Epics[0].Features);
        Assert.Equal(2, feature.Stories.Count);
        Assert.Equal("S1", feature.Stories[0].Title);
    }

    [Fact]
    public async Task UpdateStory_ChangesFields()
    {
        var repoId = await SeedRepoAsync();
        Guid storyId;
        await using (var ctx = NewContext())
        {
            var svc = NewService(ctx);
            var e = await svc.AddEpicAsync(repoId, new CreateEpicRequest("E", null, null, null), "alice");
            var f = await svc.AddFeatureAsync(e!.Id, new CreateFeatureRequest("F", null, null, null), "alice");
            var s = await svc.AddStoryAsync(f!.Id, new CreateUserStoryRequest("orig", null, null, null, null, null), "alice");
            storyId = s!.Id;
        }

        await using (var ctx = NewContext())
        {
            var dto = await NewService(ctx).UpdateStoryAsync(
                storyId,
                new UpdateUserStoryRequest("updated", "new desc", null, 5, null),
                "alice");
            Assert.NotNull(dto);
            Assert.Equal("updated", dto!.Title);
            Assert.Equal(5, dto.StoryPoints);
        }
    }

    [Fact]
    public async Task UpdateStoryStatus_ValidTransition_UpdatesStatusAndPrUrl()
    {
        var repoId = await SeedRepoAsync();
        Guid storyId;
        await using (var ctx = NewContext())
        {
            var svc = NewService(ctx);
            var e = await svc.AddEpicAsync(repoId, new CreateEpicRequest("E", null, null, null), "alice");
            var f = await svc.AddFeatureAsync(e!.Id, new CreateFeatureRequest("F", null, null, null), "alice");
            var s = await svc.AddStoryAsync(f!.Id, new CreateUserStoryRequest("S", null, null, null, null, null), "alice");
            storyId = s!.Id;
        }

        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).UpdateStoryStatusAsync(
                storyId,
                new UpdateUserStoryStatusRequest("InReview", "https://github.com/x/y/pull/1"),
                "alice");
            Assert.Equal(UserStoryStatusUpdateOutcome.Updated, result.Outcome);
            Assert.NotNull(result.Story);
            Assert.Equal("InReview", result.Story!.Status);
            Assert.Equal("https://github.com/x/y/pull/1", result.Story.PullRequestUrl);
        }
    }

    [Fact]
    public async Task UpdateStoryStatus_DoneToDraft_ReturnsInvalidTransition()
    {
        var repoId = await SeedRepoAsync();
        Guid storyId;
        await using (var ctx = NewContext())
        {
            var svc = NewService(ctx);
            var e = await svc.AddEpicAsync(repoId, new CreateEpicRequest("E", null, null, null), "alice");
            var f = await svc.AddFeatureAsync(e!.Id, new CreateFeatureRequest("F", null, null, null), "alice");
            var s = await svc.AddStoryAsync(f!.Id, new CreateUserStoryRequest("S", null, null, null, null, null), "alice");
            storyId = s!.Id;

            await NewService(ctx).UpdateStoryStatusAsync(storyId, new UpdateUserStoryStatusRequest("Done", null), "alice");
        }

        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).UpdateStoryStatusAsync(
                storyId,
                new UpdateUserStoryStatusRequest("Draft", null),
                "alice");
            Assert.Equal(UserStoryStatusUpdateOutcome.InvalidTransition, result.Outcome);
            Assert.Null(result.Story);
            Assert.Contains("Done", result.Error);
        }
    }

    [Fact]
    public async Task UpdateStoryStatus_UnknownStatusString_ReturnsInvalidStatus()
    {
        var repoId = await SeedRepoAsync();
        Guid storyId;
        await using (var ctx = NewContext())
        {
            var svc = NewService(ctx);
            var e = await svc.AddEpicAsync(repoId, new CreateEpicRequest("E", null, null, null), "alice");
            var f = await svc.AddFeatureAsync(e!.Id, new CreateFeatureRequest("F", null, null, null), "alice");
            var s = await svc.AddStoryAsync(f!.Id, new CreateUserStoryRequest("S", null, null, null, null, null), "alice");
            storyId = s!.Id;
        }

        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).UpdateStoryStatusAsync(
                storyId,
                new UpdateUserStoryStatusRequest("Bogus", null),
                "alice");
            Assert.Equal(UserStoryStatusUpdateOutcome.InvalidStatus, result.Outcome);
        }
    }

    [Fact]
    public async Task UpdateStoryStatus_Stranger_ReturnsNotFound()
    {
        var repoId = await SeedRepoAsync();
        Guid storyId;
        await using (var ctx = NewContext())
        {
            var svc = NewService(ctx);
            var e = await svc.AddEpicAsync(repoId, new CreateEpicRequest("E", null, null, null), "alice");
            var f = await svc.AddFeatureAsync(e!.Id, new CreateFeatureRequest("F", null, null, null), "alice");
            var s = await svc.AddStoryAsync(f!.Id, new CreateUserStoryRequest("S", null, null, null, null, null), "alice");
            storyId = s!.Id;
        }

        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).UpdateStoryStatusAsync(
                storyId,
                new UpdateUserStoryStatusRequest("Ready", null),
                "mallory");
            Assert.Equal(UserStoryStatusUpdateOutcome.NotFound, result.Outcome);
        }
    }

    [Fact]
    public async Task DeleteEpic_CascadesFeaturesAndStories()
    {
        var repoId = await SeedRepoAsync();
        Guid epicId, storyId;
        await using (var ctx = NewContext())
        {
            var svc = NewService(ctx);
            var e = await svc.AddEpicAsync(repoId, new CreateEpicRequest("E", null, null, null), "alice");
            epicId = e!.Id;
            var f = await svc.AddFeatureAsync(epicId, new CreateFeatureRequest("F", null, null, null), "alice");
            var s = await svc.AddStoryAsync(f!.Id, new CreateUserStoryRequest("S", null, null, null, null, null), "alice");
            storyId = s!.Id;
        }

        await using (var ctx = NewContext())
        {
            var ok = await NewService(ctx).DeleteEpicAsync(epicId, "alice");
            Assert.True(ok);
        }

        await using (var ctx = NewContext())
        {
            Assert.Null(await ctx.Epics.FindAsync(epicId));
            Assert.Null(await ctx.UserStories.FindAsync(storyId));
        }
    }
}
