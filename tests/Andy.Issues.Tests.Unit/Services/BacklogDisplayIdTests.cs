// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

// AH1 — DisplayId round-trip tests.
//
// Verifies that entities created through BacklogService carry a
// stable `EPIC-{n}` / `FEAT-{n}` / `STORY-{n}` identifier and that
// the computed getter pairs with the persisted `Seq` column.
public class BacklogDisplayIdTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BacklogDisplayIdTests()
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

    [Fact]
    public void Epic_DisplayId_matches_Seq()
    {
        var epic = new Epic { Seq = 42 };
        Assert.Equal("EPIC-42", epic.DisplayId);
    }

    [Fact]
    public void Feature_DisplayId_matches_Seq()
    {
        var feature = new Feature { Seq = 7 };
        Assert.Equal("FEAT-7", feature.DisplayId);
    }

    [Fact]
    public void UserStory_DisplayId_matches_Seq()
    {
        var story = new UserStory { Seq = 13 };
        Assert.Equal("STORY-13", story.DisplayId);
    }

    [Fact]
    public async Task BacklogService_AddEpic_allocates_monotonic_Seq()
    {
        var repoId = await SeedRepoAsync();

        await using var ctx = NewContext();
        var svc = new BacklogService(
            ctx,
            new RepositoryAccessGuard(ctx),
            new BacklogSequenceAllocator(ctx));

        await svc.AddEpicAsync(repoId, new CreateEpicRequest("first", null, null, null), "alice");
        await svc.AddEpicAsync(repoId, new CreateEpicRequest("second", null, null, null), "alice");
        await svc.AddEpicAsync(repoId, new CreateEpicRequest("third", null, null, null), "alice");

        await using var verify = NewContext();
        var seqs = verify.Epics.OrderBy(e => e.Seq).Select(e => e.Seq).ToList();
        Assert.Equal(new[] { 1L, 2L, 3L }, seqs);
    }

    [Fact]
    public async Task BacklogService_AddFeature_and_AddStory_use_independent_sequences()
    {
        var repoId = await SeedRepoAsync();

        await using var ctx = NewContext();
        var svc = new BacklogService(
            ctx,
            new RepositoryAccessGuard(ctx),
            new BacklogSequenceAllocator(ctx));

        var epic = await svc.AddEpicAsync(repoId, new CreateEpicRequest("e", null, null, null), "alice");
        Assert.NotNull(epic);
        var feature = await svc.AddFeatureAsync(epic!.Id, new CreateFeatureRequest("f", null, null, null), "alice");
        Assert.NotNull(feature);
        await svc.AddStoryAsync(feature!.Id, new CreateUserStoryRequest("s", null, null, null, null, null), "alice");

        await using var verify = NewContext();
        Assert.Equal(1, verify.Epics.Single().Seq);
        Assert.Equal(1, verify.Features.Single().Seq);
        Assert.Equal(1, verify.UserStories.Single().Seq);
    }

    private async Task<Guid> SeedRepoAsync()
    {
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "repo",
            CloneUrl = "https://example.com/r.git"
        };
        ctx.Repositories.Add(repo);
        await ctx.SaveChangesAsync();
        return repo.Id;
    }
}
