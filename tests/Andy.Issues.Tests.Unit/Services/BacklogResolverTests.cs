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

// AH1 — BacklogService.Get{Epic,Feature,Story}Async round-trips.
//
// Verifies that the new display-id-aware resolvers locate the same
// entity regardless of whether the caller provides the GUID or the
// `EPIC-n` / `FEAT-n` / `STORY-n` short form, and reject shapes
// scoped to a different entity type (e.g. passing `FEAT-1` to
// GetEpicAsync).
public class BacklogResolverTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BacklogResolverTests()
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
        new(ctx, new RepositoryAccessGuard(ctx), new BacklogSequenceAllocator(ctx));

    [Fact]
    public async Task GetEpic_resolves_by_guid_and_display_id()
    {
        var repoId = await SeedRepoAsync();

        await using var ctx = NewContext();
        var svc = NewService(ctx);
        var created = await svc.AddEpicAsync(repoId, new CreateEpicRequest("goal", null, null, null), "alice");
        Assert.NotNull(created);

        var byGuid = await svc.GetEpicAsync(created!.Id.ToString(), "alice");
        var byDisplay = await svc.GetEpicAsync(created.DisplayId, "alice");

        Assert.NotNull(byGuid);
        Assert.NotNull(byDisplay);
        Assert.Equal(created.Id, byGuid!.Id);
        Assert.Equal(created.Id, byDisplay!.Id);
        Assert.Equal("EPIC-1", byDisplay.DisplayId);
    }

    [Fact]
    public async Task GetFeature_resolves_by_display_id()
    {
        var repoId = await SeedRepoAsync();

        await using var ctx = NewContext();
        var svc = NewService(ctx);
        var epic = await svc.AddEpicAsync(repoId, new CreateEpicRequest("e", null, null, null), "alice");
        var feature = await svc.AddFeatureAsync(epic!.Id, new CreateFeatureRequest("f", null, null, null), "alice");

        var resolved = await svc.GetFeatureAsync(feature!.DisplayId, "alice");

        Assert.NotNull(resolved);
        Assert.Equal("FEAT-1", resolved!.DisplayId);
        Assert.Equal(feature.Id, resolved.Id);
    }

    [Fact]
    public async Task GetStory_resolves_by_display_id()
    {
        var repoId = await SeedRepoAsync();

        await using var ctx = NewContext();
        var svc = NewService(ctx);
        var epic = await svc.AddEpicAsync(repoId, new CreateEpicRequest("e", null, null, null), "alice");
        var feature = await svc.AddFeatureAsync(epic!.Id, new CreateFeatureRequest("f", null, null, null), "alice");
        var story = await svc.AddStoryAsync(feature!.Id, new CreateUserStoryRequest("s", null, null, null, null, null), "alice");

        var resolved = await svc.GetStoryAsync(story!.DisplayId, "alice");

        Assert.NotNull(resolved);
        Assert.Equal("STORY-1", resolved!.DisplayId);
        Assert.Equal(story.Id, resolved.Id);
    }

    [Fact]
    public async Task GetEpic_rejects_wrong_type_prefix()
    {
        var repoId = await SeedRepoAsync();

        await using var ctx = NewContext();
        var svc = NewService(ctx);
        var epic = await svc.AddEpicAsync(repoId, new CreateEpicRequest("e", null, null, null), "alice");
        Assert.NotNull(epic);

        // A feature-shaped id must not resolve via the epic endpoint
        // even when an epic with that seq exists.
        Assert.Null(await svc.GetEpicAsync("FEAT-1", "alice"));
        Assert.Null(await svc.GetEpicAsync("STORY-1", "alice"));
    }

    [Fact]
    public async Task GetEpic_returns_null_for_nonexistent_id()
    {
        await SeedRepoAsync();

        await using var ctx = NewContext();
        var svc = NewService(ctx);

        Assert.Null(await svc.GetEpicAsync("EPIC-999", "alice"));
        Assert.Null(await svc.GetEpicAsync(Guid.NewGuid().ToString(), "alice"));
        Assert.Null(await svc.GetEpicAsync("garbage", "alice"));
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
