// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

// Verifies that BacklogService writes the correct andy.issues.events.
// story.* outbox row for each lifecycle transition (Story 15.3).
public class BacklogServiceStoryEventsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BacklogServiceStoryEventsTests()
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
    private BacklogService NewService(AppDbContext ctx) => new(ctx, new RepositoryAccessGuard(ctx));

    private async Task<(Guid repoId, Guid epicId, Guid featureId)> SeedScaffoldAsync()
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

        var epic = new Epic
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            Title = "E",
            Order = 1
        };
        ctx.Epics.Add(epic);

        var feature = new Feature
        {
            Id = Guid.NewGuid(),
            EpicId = epic.Id,
            Title = "F",
            Order = 1
        };
        ctx.Features.Add(feature);

        await ctx.SaveChangesAsync();
        return (repo.Id, epic.Id, feature.Id);
    }

    [Fact]
    public async Task AddStory_EmitsCreatedEvent()
    {
        var (repoId, epicId, featureId) = await SeedScaffoldAsync();
        await using (var ctx = NewContext())
        {
            var dto = await NewService(ctx).AddStoryAsync(featureId,
                new CreateUserStoryRequest("A story", null, null, null, null, null), "alice");
            Assert.NotNull(dto);
        }

        await using var verify = NewContext();
        var entry = await verify.Outbox.SingleAsync();
        Assert.EndsWith(".created", entry.Subject);
        Assert.StartsWith("andy.issues.events.story.", entry.Subject);

        using var doc = JsonDocument.Parse(entry.PayloadJson);
        var root = doc.RootElement;
        Assert.Equal(featureId.ToString(), root.GetProperty("feature_id").GetString());
        Assert.Equal(epicId.ToString(), root.GetProperty("epic_id").GetString());
        Assert.Equal(repoId.ToString(), root.GetProperty("repository_id").GetString());
        Assert.Equal("A story", root.GetProperty("title").GetString());
        Assert.Equal("Draft", root.GetProperty("status").GetString());
        Assert.Equal(StoryEventPayload.SchemaVersion, root.GetProperty("schema_version").GetInt32());
    }

    [Fact]
    public async Task UpdateStoryStatus_ToReady_EmitsReadiedEvent()
    {
        var (_, _, featureId) = await SeedScaffoldAsync();
        Guid storyId;
        await using (var ctx = NewContext())
        {
            var dto = await NewService(ctx).AddStoryAsync(featureId,
                new CreateUserStoryRequest("s", null, null, null, null, null), "alice");
            storyId = dto!.Id;
        }

        await using (var ctx = NewContext())
        {
            await NewService(ctx).UpdateStoryStatusAsync(storyId,
                new UpdateUserStoryStatusRequest("Ready", null), "alice");
        }

        await using var verify = NewContext();
        var entries = await verify.Outbox.OrderBy(e => e.CreatedAt).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.EndsWith(".created", entries[0].Subject);
        Assert.EndsWith(".readied", entries[1].Subject);
    }

    [Fact]
    public async Task UpdateStoryStatus_ToDone_EmitsDoneEvent()
    {
        var (_, _, featureId) = await SeedScaffoldAsync();
        Guid storyId;
        await using (var ctx = NewContext())
        {
            storyId = (await NewService(ctx).AddStoryAsync(featureId,
                new CreateUserStoryRequest("s", null, null, null, null, null), "alice"))!.Id;
        }

        // Ready first (cannot jump Draft → Done directly via SetStatus?
        // Actually SetStatus only blocks Done → Draft. Going Draft → Done
        // is allowed. Verify behaviour by transitioning directly.)
        await using (var ctx = NewContext())
        {
            await NewService(ctx).UpdateStoryStatusAsync(storyId,
                new UpdateUserStoryStatusRequest("Done", null), "alice");
        }

        await using var verify = NewContext();
        var last = await verify.Outbox.OrderByDescending(e => e.CreatedAt).FirstAsync();
        Assert.EndsWith(".done", last.Subject);

        using var doc = JsonDocument.Parse(last.PayloadJson);
        Assert.Equal("Done", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UpdateStoryStatus_ToInProgress_EmitsUpdatedEvent()
    {
        var (_, _, featureId) = await SeedScaffoldAsync();
        Guid storyId;
        await using (var ctx = NewContext())
        {
            storyId = (await NewService(ctx).AddStoryAsync(featureId,
                new CreateUserStoryRequest("s", null, null, null, null, null), "alice"))!.Id;
        }

        await using (var ctx = NewContext())
        {
            await NewService(ctx).UpdateStoryStatusAsync(storyId,
                new UpdateUserStoryStatusRequest("InProgress", null), "alice");
        }

        await using var verify = NewContext();
        var last = await verify.Outbox.OrderByDescending(e => e.CreatedAt).FirstAsync();
        Assert.EndsWith(".updated", last.Subject);
    }

    [Fact]
    public async Task UpdateStory_EmitsUpdatedEvent()
    {
        var (_, _, featureId) = await SeedScaffoldAsync();
        Guid storyId;
        await using (var ctx = NewContext())
        {
            storyId = (await NewService(ctx).AddStoryAsync(featureId,
                new CreateUserStoryRequest("s", null, null, null, null, null), "alice"))!.Id;
        }

        await using (var ctx = NewContext())
        {
            await NewService(ctx).UpdateStoryAsync(storyId,
                new UpdateUserStoryRequest("new title", null, null, null, null), "alice");
        }

        await using var verify = NewContext();
        var last = await verify.Outbox.OrderByDescending(e => e.CreatedAt).FirstAsync();
        Assert.EndsWith(".updated", last.Subject);

        using var doc = JsonDocument.Parse(last.PayloadJson);
        Assert.Equal("new title", doc.RootElement.GetProperty("title").GetString());
    }
}
