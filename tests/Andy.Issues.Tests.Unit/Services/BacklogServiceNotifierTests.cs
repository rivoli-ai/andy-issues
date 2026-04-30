// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class BacklogServiceNotifierTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly RecordingBoardNotifier _notifier = new();

    public BacklogServiceNotifierTests()
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
        new(ctx, new RepositoryAccessGuard(ctx), new BacklogSequenceAllocator(ctx), _notifier);

    private async Task<Guid> SeedRepoAsync()
    {
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "r",
            CloneUrl = "https://example.com/r.git"
        };
        ctx.Repositories.Add(repo);
        await ctx.SaveChangesAsync();
        return repo.Id;
    }

    [Fact]
    public async Task AddEpic_EmitsEpicAdded()
    {
        var repoId = await SeedRepoAsync();
        await using var ctx = NewContext();
        var dto = await NewService(ctx).AddEpicAsync(repoId, new CreateEpicRequest("E", null, null, null), "alice");
        Assert.NotNull(dto);
        var evt = Assert.Single(_notifier.Events);
        Assert.Equal("EpicAdded", evt.kind);
        Assert.Equal(repoId, evt.repositoryId);
    }

    [Fact]
    public async Task FullCycle_EmitsExpectedEventStream()
    {
        var repoId = await SeedRepoAsync();
        await using (var ctx = NewContext())
        {
            var svc = NewService(ctx);
            var e = await svc.AddEpicAsync(repoId, new CreateEpicRequest("E", null, null, null), "alice");
            var f = await svc.AddFeatureAsync(e!.Id, new CreateFeatureRequest("F", null, null, null), "alice");
            var s = await svc.AddStoryAsync(f!.Id, new CreateUserStoryRequest("S", null, null, null, null, null), "alice");
            await svc.UpdateStoryStatusAsync(
                s!.Id,
                new UpdateUserStoryStatusRequest("Ready", null),
                "alice");
            await svc.DeleteStoryAsync(s.Id, "alice");
        }

        var kinds = _notifier.Events.Select(ev => ev.kind).ToList();
        Assert.Equal(
            new[] { "EpicAdded", "FeatureAdded", "StoryAdded", "StoryUpdated", "StoryDeleted" },
            kinds);
        Assert.All(_notifier.Events, ev => Assert.Equal(repoId, ev.repositoryId));
    }

    [Fact]
    public async Task Delete_FailedAccess_DoesNotEmit()
    {
        var repoId = await SeedRepoAsync();
        Guid epicId;
        await using (var ctx = NewContext())
        {
            var svc = NewService(ctx);
            var e = await svc.AddEpicAsync(repoId, new CreateEpicRequest("E", null, null, null), "alice");
            epicId = e!.Id;
        }
        _notifier.Events.Clear();

        await using (var ctx = NewContext())
        {
            var ok = await NewService(ctx).DeleteEpicAsync(epicId, "mallory");
            Assert.False(ok);
        }

        Assert.Empty(_notifier.Events);
    }

    private sealed class RecordingBoardNotifier : IBoardNotifier
    {
        public List<(string kind, Guid repositoryId, object? payload)> Events { get; } = new();

        private Task Record(string kind, Guid repositoryId, object? payload)
        {
            Events.Add((kind, repositoryId, payload));
            return Task.CompletedTask;
        }

        public Task EpicAddedAsync(Guid r, EpicDto e, CancellationToken ct = default) => Record("EpicAdded", r, e);
        public Task EpicUpdatedAsync(Guid r, EpicDto e, CancellationToken ct = default) => Record("EpicUpdated", r, e);
        public Task EpicDeletedAsync(Guid r, Guid id, CancellationToken ct = default) => Record("EpicDeleted", r, id);
        public Task FeatureAddedAsync(Guid r, FeatureDto f, CancellationToken ct = default) => Record("FeatureAdded", r, f);
        public Task FeatureUpdatedAsync(Guid r, FeatureDto f, CancellationToken ct = default) => Record("FeatureUpdated", r, f);
        public Task FeatureDeletedAsync(Guid r, Guid id, CancellationToken ct = default) => Record("FeatureDeleted", r, id);
        public Task StoryAddedAsync(Guid r, UserStoryDto s, CancellationToken ct = default) => Record("StoryAdded", r, s);
        public Task StoryUpdatedAsync(Guid r, UserStoryDto s, CancellationToken ct = default) => Record("StoryUpdated", r, s);
        public Task StoryDeletedAsync(Guid r, Guid id, CancellationToken ct = default) => Record("StoryDeleted", r, id);
        public Task BacklogGenerationProgressAsync(Guid r, BacklogGenerationDto g, CancellationToken ct = default) => Record("BacklogGenerationProgress", r, g);
    }
}
