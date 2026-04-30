// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class BacklogGenerationTrackerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BacklogGenerationTrackerTests()
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

    private async Task<Guid> SeedRepoAsync(string owner = "alice")
    {
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner,
            Name = "repo",
            CloneUrl = "https://example.com/r.git"
        };
        ctx.Repositories.Add(repo);
        await ctx.SaveChangesAsync();
        return repo.Id;
    }

    [Fact]
    public async Task Start_PersistsPendingRowAndPushesProgress()
    {
        var repoId = await SeedRepoAsync();
        var notifier = new RecordingNotifier();
        await using var ctx = NewContext();
        var tracker = new BacklogGenerationTracker(ctx, notifier);

        var dto = await tracker.StartAsync(repoId, "alice");

        Assert.Equal("Pending", dto.Phase);
        Assert.Equal(repoId, dto.RepositoryId);
        Assert.Single(notifier.Pushes);
        Assert.Equal(repoId, notifier.Pushes[0].repoId);
        Assert.Equal("Pending", notifier.Pushes[0].generation.Phase);

        await using var verify = NewContext();
        var row = await verify.BacklogGenerations.SingleAsync();
        Assert.Equal(BacklogGenerationPhase.Pending, row.Phase);
        Assert.Null(row.CompletedAt);
    }

    [Fact]
    public async Task Advance_NonTerminal_BumpsUpdatedAt_NoCompletedAt()
    {
        var repoId = await SeedRepoAsync();
        var notifier = new RecordingNotifier();

        Guid genId;
        await using (var ctx = NewContext())
            genId = (await new BacklogGenerationTracker(ctx, notifier)
                .StartAsync(repoId, "alice")).Id;

        await Task.Delay(5); // ensure UpdatedAt strictly advances
        await using (var ctx = NewContext())
        {
            var dto = await new BacklogGenerationTracker(ctx, notifier)
                .AdvanceAsync(genId, BacklogGenerationPhase.CallingLlm, "Calling gpt-4o");
            Assert.NotNull(dto);
            Assert.Equal("CallingLlm", dto!.Phase);
            Assert.Equal("Calling gpt-4o", dto.Detail);
            Assert.Null(dto.CompletedAt);
        }

        Assert.Equal(2, notifier.Pushes.Count);
    }

    [Fact]
    public async Task Advance_TerminalPhase_StampsCompletedAt()
    {
        var repoId = await SeedRepoAsync();

        Guid genId;
        await using (var ctx = NewContext())
            genId = (await new BacklogGenerationTracker(ctx, null)
                .StartAsync(repoId, "alice")).Id;

        await using (var ctx = NewContext())
            await new BacklogGenerationTracker(ctx, null)
                .AdvanceAsync(genId, BacklogGenerationPhase.Completed);

        await using var verify = NewContext();
        var row = await verify.BacklogGenerations.SingleAsync(g => g.Id == genId);
        Assert.Equal(BacklogGenerationPhase.Completed, row.Phase);
        Assert.NotNull(row.CompletedAt);
    }

    [Fact]
    public async Task Advance_UnknownGeneration_ReturnsNull()
    {
        await using var ctx = NewContext();
        var dto = await new BacklogGenerationTracker(ctx, null)
            .AdvanceAsync(Guid.NewGuid(), BacklogGenerationPhase.CallingLlm);
        Assert.Null(dto);
    }

    [Fact]
    public async Task Get_OwnerOnly_StrangerSeesNull()
    {
        var repoId = await SeedRepoAsync(owner: "alice");

        Guid genId;
        await using (var ctx = NewContext())
            genId = (await new BacklogGenerationTracker(ctx, null)
                .StartAsync(repoId, "alice")).Id;

        await using var ctx2 = NewContext();
        var tracker = new BacklogGenerationTracker(ctx2, null);

        Assert.NotNull(await tracker.GetAsync(genId, "alice"));
        Assert.Null(await tracker.GetAsync(genId, "bob"));
    }

    private sealed class RecordingNotifier : IBoardNotifier
    {
        public List<(Guid repoId, BacklogGenerationDto generation)> Pushes { get; } = new();

        public Task EpicAddedAsync(Guid r, EpicDto e, CancellationToken ct = default) => Task.CompletedTask;
        public Task EpicUpdatedAsync(Guid r, EpicDto e, CancellationToken ct = default) => Task.CompletedTask;
        public Task EpicDeletedAsync(Guid r, Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task FeatureAddedAsync(Guid r, FeatureDto f, CancellationToken ct = default) => Task.CompletedTask;
        public Task FeatureUpdatedAsync(Guid r, FeatureDto f, CancellationToken ct = default) => Task.CompletedTask;
        public Task FeatureDeletedAsync(Guid r, Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task StoryAddedAsync(Guid r, UserStoryDto s, CancellationToken ct = default) => Task.CompletedTask;
        public Task StoryUpdatedAsync(Guid r, UserStoryDto s, CancellationToken ct = default) => Task.CompletedTask;
        public Task StoryDeletedAsync(Guid r, Guid id, CancellationToken ct = default) => Task.CompletedTask;

        public Task BacklogGenerationProgressAsync(Guid r, BacklogGenerationDto g, CancellationToken ct = default)
        {
            Pushes.Add((r, g));
            return Task.CompletedTask;
        }
    }
}
