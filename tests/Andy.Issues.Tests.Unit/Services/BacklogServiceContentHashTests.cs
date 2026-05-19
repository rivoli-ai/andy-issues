// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Services;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

// SP.7.1 (andy-issues#181 / conductor#1627) — integration-style
// verification that ContentHash is wired all the way through:
//   - Persisted on UserStory.ContentHash by the SaveChanges hook.
//   - Surfaced on UserStoryDto for REST/gRPC/SignalR consumers.
//   - Emitted in the outbox StoryEventPayload so andy-tasks +
//     Conductor can detect drift after re-import.
//   - Lazily backfilled for rows that pre-date the migration.
public class BacklogServiceContentHashTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BacklogServiceContentHashTests()
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

        var epic = new Epic { Id = Guid.NewGuid(), RepositoryId = repo.Id, Title = "E", Order = 1 };
        ctx.Epics.Add(epic);

        var feature = new Feature { Id = Guid.NewGuid(), EpicId = epic.Id, Title = "F", Order = 1 };
        ctx.Features.Add(feature);

        await ctx.SaveChangesAsync();
        return (repo.Id, epic.Id, feature.Id);
    }

    [Fact]
    public async Task AddStory_PersistsContentHash()
    {
        var (_, _, featureId) = await SeedScaffoldAsync();

        Guid storyId;
        await using (var ctx = NewContext())
        {
            var dto = await NewService(ctx).AddStoryAsync(featureId,
                new CreateUserStoryRequest("A story", "desc", "Given X", null, null, null),
                "alice");
            Assert.NotNull(dto);
            storyId = dto!.Id;
        }

        await using var verify = NewContext();
        var stored = await verify.UserStories.AsNoTracking().FirstAsync(s => s.Id == storyId);
        Assert.NotNull(stored.ContentHash);
        Assert.Equal(64, stored.ContentHash!.Length);

        var expected = StoryContentHasher.Compute("A story", "desc", null, "Given X");
        Assert.Equal(expected, stored.ContentHash);
    }

    [Fact]
    public async Task AddStory_DtoCarriesContentHash()
    {
        var (_, _, featureId) = await SeedScaffoldAsync();

        await using var ctx = NewContext();
        var dto = await NewService(ctx).AddStoryAsync(featureId,
            new CreateUserStoryRequest("A story", "desc", null, null, null, null),
            "alice");

        Assert.NotNull(dto);
        Assert.NotNull(dto!.ContentHash);
        Assert.Equal(StoryContentHasher.Compute("A story", "desc", null, null), dto.ContentHash);
    }

    [Fact]
    public async Task AddStory_OutboxPayloadCarriesContentHash()
    {
        var (_, _, featureId) = await SeedScaffoldAsync();

        await using (var ctx = NewContext())
        {
            await NewService(ctx).AddStoryAsync(featureId,
                new CreateUserStoryRequest("A story", "desc", null, null, null, null),
                "alice");
        }

        await using var verify = NewContext();
        var entry = await verify.Outbox.SingleAsync();
        using var doc = JsonDocument.Parse(entry.PayloadJson);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("content_hash", out var hashEl),
            "story.* outbox payload must carry content_hash for drift detection (SP.7.1).");
        var hash = hashEl.GetString();
        Assert.NotNull(hash);
        Assert.Equal(StoryContentHasher.Compute("A story", "desc", null, null), hash);
    }

    [Fact]
    public async Task UpdateStory_RecomputesHashOnEverySave()
    {
        var (_, _, featureId) = await SeedScaffoldAsync();
        Guid storyId;
        await using (var ctx = NewContext())
        {
            storyId = (await NewService(ctx).AddStoryAsync(featureId,
                new CreateUserStoryRequest("v1", "body", null, null, null, null),
                "alice"))!.Id;
        }

        string? hashV1;
        await using (var ctx = NewContext())
        {
            hashV1 = (await ctx.UserStories.AsNoTracking().FirstAsync(s => s.Id == storyId)).ContentHash;
        }

        await using (var ctx = NewContext())
        {
            await NewService(ctx).UpdateStoryAsync(storyId,
                new UpdateUserStoryRequest("v2", null, null, null, null),
                "alice");
        }

        await using var verify = NewContext();
        var hashV2 = (await verify.UserStories.AsNoTracking().FirstAsync(s => s.Id == storyId)).ContentHash;

        Assert.NotEqual(hashV1, hashV2);
        Assert.Equal(StoryContentHasher.Compute("v2", "body", null, null), hashV2);
    }

    [Fact]
    public async Task UpdateStory_WhitespaceOnlyChangeDoesNotChangeHash()
    {
        var (_, _, featureId) = await SeedScaffoldAsync();
        Guid storyId;
        await using (var ctx = NewContext())
        {
            storyId = (await NewService(ctx).AddStoryAsync(featureId,
                new CreateUserStoryRequest("v1", "body", null, null, null, null),
                "alice"))!.Id;
        }

        string? hashBefore;
        await using (var ctx = NewContext())
        {
            hashBefore = (await ctx.UserStories.AsNoTracking().FirstAsync(s => s.Id == storyId)).ContentHash;
        }

        await using (var ctx = NewContext())
        {
            await NewService(ctx).UpdateStoryAsync(storyId,
                new UpdateUserStoryRequest("  v1  ", "body\n\n", null, null, null),
                "alice");
        }

        await using var verify = NewContext();
        var hashAfter = (await verify.UserStories.AsNoTracking().FirstAsync(s => s.Id == storyId)).ContentHash;

        Assert.Equal(hashBefore, hashAfter);
    }

    [Fact]
    public async Task UpdateStory_OutboxPayloadCarriesUpdatedContentHash()
    {
        var (_, _, featureId) = await SeedScaffoldAsync();
        Guid storyId;
        await using (var ctx = NewContext())
        {
            storyId = (await NewService(ctx).AddStoryAsync(featureId,
                new CreateUserStoryRequest("v1", null, null, null, null, null),
                "alice"))!.Id;
        }

        await using (var ctx = NewContext())
        {
            await NewService(ctx).UpdateStoryAsync(storyId,
                new UpdateUserStoryRequest("v2", null, null, null, null),
                "alice");
        }

        await using var verify = NewContext();
        var last = await verify.Outbox.OrderByDescending(e => e.CreatedAt).FirstAsync();
        using var doc = JsonDocument.Parse(last.PayloadJson);
        var hash = doc.RootElement.GetProperty("content_hash").GetString();

        Assert.Equal(StoryContentHasher.Compute("v2", null, null, null), hash);
    }

    [Fact]
    public async Task GetStory_BackfillsContentHashFromLegacyRow()
    {
        // Simulates an existing row created before SP.7.1 landed — the
        // ContentHash column is NULL on disk. The application-layer
        // mapping must recompute on the fly so consumers never see a
        // NULL hash. (The persisted column gets backfilled the next
        // time the story is mutated, by the SaveChanges hook.)
        var (_, _, featureId) = await SeedScaffoldAsync();

        Guid storyId;
        await using (var ctx = NewContext())
        {
            var story = new UserStory
            {
                Id = Guid.NewGuid(),
                Seq = 999,
                FeatureId = featureId,
                Title = "Legacy",
                Description = "Pre-migration content",
                Order = 1
            };
            ctx.UserStories.Add(story);
            await ctx.SaveChangesAsync();
            storyId = story.Id;

            // Forcibly null out the column so we can observe the lazy
            // backfill path in the mapping. (Bypass the SaveChanges
            // hook with a raw SQL update.)
            await ctx.Database.ExecuteSqlRawAsync(
                "UPDATE UserStories SET ContentHash = NULL WHERE Id = {0}", storyId);
        }

        await using (var verify = NewContext())
        {
            var raw = await verify.UserStories.AsNoTracking().FirstAsync(s => s.Id == storyId);
            Assert.Null(raw.ContentHash);
        }

        await using (var ctx = NewContext())
        {
            var dto = await NewService(ctx).GetStoryAsync(storyId.ToString(), "alice");
            Assert.NotNull(dto);
            Assert.NotNull(dto!.ContentHash);
            Assert.Equal(
                StoryContentHasher.Compute("Legacy", "Pre-migration content", null, null),
                dto.ContentHash);
        }
    }

    [Fact]
    public async Task PayloadDeserializesContentHashRoundTrip()
    {
        // Belt-and-braces: confirm StoryEventPayload itself round-trips
        // the new field through the snake_case outbox serializer.
        var (_, _, featureId) = await SeedScaffoldAsync();

        await using (var ctx = NewContext())
        {
            await NewService(ctx).AddStoryAsync(featureId,
                new CreateUserStoryRequest("Round trip", null, null, null, null, null),
                "alice");
        }

        await using var verify = NewContext();
        var entry = await verify.Outbox.SingleAsync();
        var typed = JsonSerializer.Deserialize<StoryEventPayload>(
            entry.PayloadJson, Andy.Issues.Application.Messaging.EventJson.Options);

        Assert.NotNull(typed);
        Assert.NotNull(typed!.ContentHash);
        Assert.Equal(StoryContentHasher.Compute("Round trip", null, null, null), typed.ContentHash);
    }
}
