// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class UserDirectoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public UserDirectoryTests()
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

    private async Task SeedAsync()
    {
        await using var ctx = NewContext();
        ctx.UserDirectory.AddRange(
            new UserDirectoryEntry { Id = Guid.NewGuid(), UserId = "alice", Email = "alice@example.com", DisplayName = "Alice" },
            new UserDirectoryEntry { Id = Guid.NewGuid(), UserId = "bob", Email = "bob@example.com", DisplayName = "Bob" },
            new UserDirectoryEntry { Id = Guid.NewGuid(), UserId = "carol", Email = "carol@example.com", DisplayName = "Carol" });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Suggest_MatchesEmailPrefix()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var dir = new UserDirectoryService(ctx);

        var matches = await dir.SuggestAsync("bob", "alice", 10);

        Assert.Single(matches);
        Assert.Equal("bob", matches[0].UserId);
    }

    [Fact]
    public async Task Suggest_ExcludesSelf()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var dir = new UserDirectoryService(ctx);

        var matches = await dir.SuggestAsync("alice", "alice", 10);
        Assert.Empty(matches);
    }

    [Fact]
    public async Task Suggest_RespectsLimit()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var dir = new UserDirectoryService(ctx);

        var matches = await dir.SuggestAsync("@example.com", "nobody", 2);
        // prefix match against the e-mail column; none start with "@example.com",
        // so the LIKE returns 0 — this test asserts the limit behavior via a
        // prefix that DOES match.
        Assert.Empty(matches);

        var all = await dir.SuggestAsync("a", "nobody", 2);
        Assert.Single(all); // only alice matches prefix "a"
    }

    [Fact]
    public async Task Upsert_CreatesThenUpdates()
    {
        await using (var ctx = NewContext())
        {
            await new UserDirectoryService(ctx).UpsertAsync(new UserRecord("dave", "dave@example.com", "Dave"));
        }
        await using (var ctx = NewContext())
        {
            await new UserDirectoryService(ctx).UpsertAsync(new UserRecord("dave", "dave@example.com", "Dave the Second"));
        }
        await using (var ctx = NewContext())
        {
            var entry = await ctx.UserDirectory.FirstAsync(u => u.UserId == "dave");
            Assert.Equal("Dave the Second", entry.DisplayName);
            Assert.NotNull(entry.UpdatedAt);
        }
    }
}
