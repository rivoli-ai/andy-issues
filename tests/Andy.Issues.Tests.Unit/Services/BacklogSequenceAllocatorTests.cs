// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

// AH1 — BacklogSequenceAllocator contract tests.
//
// Exercises the UPDATE..RETURNING path on SQLite (the in-memory
// database happens to speak the same syntax as Postgres for
// purposes of this test). The concurrency / monotonicity story
// under load is covered by an integration test (AH7) where a real
// database connection pool lets us drive parallel requests.
public class BacklogSequenceAllocatorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BacklogSequenceAllocatorTests()
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
    public async Task Allocate_starts_at_1_for_each_type()
    {
        await using var ctx = NewContext();
        var allocator = new BacklogSequenceAllocator(ctx);

        Assert.Equal(1, await allocator.AllocateAsync(BacklogEntityType.Epic));
        Assert.Equal(1, await allocator.AllocateAsync(BacklogEntityType.Feature));
        Assert.Equal(1, await allocator.AllocateAsync(BacklogEntityType.Story));
    }

    [Fact]
    public async Task Allocate_is_monotonic_per_type()
    {
        await using var ctx = NewContext();
        var allocator = new BacklogSequenceAllocator(ctx);

        var values = new List<long>();
        for (int i = 0; i < 10; i++)
            values.Add(await allocator.AllocateAsync(BacklogEntityType.Epic));

        Assert.Equal(new[] { 1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L, 9L, 10L }, values);
    }

    [Fact]
    public async Task Allocate_sequences_are_independent_across_types()
    {
        await using var ctx = NewContext();
        var allocator = new BacklogSequenceAllocator(ctx);

        await allocator.AllocateAsync(BacklogEntityType.Epic);
        await allocator.AllocateAsync(BacklogEntityType.Epic);
        await allocator.AllocateAsync(BacklogEntityType.Epic);

        // Story counter should still be at 1 regardless of Epic activity.
        Assert.Equal(1, await allocator.AllocateAsync(BacklogEntityType.Story));
        Assert.Equal(1, await allocator.AllocateAsync(BacklogEntityType.Feature));
    }

    [Fact]
    public async Task Allocate_persists_next_seq_across_context_recreation()
    {
        await using (var ctx = NewContext())
        {
            var allocator = new BacklogSequenceAllocator(ctx);
            await allocator.AllocateAsync(BacklogEntityType.Epic);
            await allocator.AllocateAsync(BacklogEntityType.Epic);
        }

        await using (var ctx = NewContext())
        {
            var allocator = new BacklogSequenceAllocator(ctx);
            Assert.Equal(3, await allocator.AllocateAsync(BacklogEntityType.Epic));
        }
    }
}
