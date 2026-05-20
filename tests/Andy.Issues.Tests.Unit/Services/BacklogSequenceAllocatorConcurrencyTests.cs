// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

// AH7.1 (rivoli-ai/conductor#1679) — concurrency + monotonicity
// guarantees for the EPIC-{n} / FEATURE-{n} / STORY-{n} / ISSUE-{n}
// display-id sequence in andy-issues.
//
// Mirrors andy-tasks/tests/Andy.Tasks.Tests.Unit/Services/
// PlanningSequenceAllocatorConcurrencyTests.cs so both repos have
// the same coverage shape — keeping them in sync makes drift visible.
//
// Runs against SQLite file-mode (not :memory:) so multiple
// connections share the same database; :memory: gives each
// connection its own private store, defeating any concurrency test.
public sealed class BacklogSequenceAllocatorConcurrencyTests : IAsyncLifetime
{
    private string _dbPath = null!;
    private DbContextOptions<AppDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(),
            $"andy-issues-allocator-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            // Shared cache so the concurrent writers see each other.
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .Options;

        using var db = new AppDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        // Clear EF Core's pooled connections so the file is unlocked.
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SerialAllocation_IsStrictlyMonotonic_NoGaps_N1000()
    {
        // AH7 AC: serial monotonicity at N=1000. The existing unit
        // test (BacklogSequenceAllocatorTests.Allocate_is_monotonic_per_type)
        // only goes to 10 — this is the load-test version that
        // catches any off-by-one or row-skipping behavior the small
        // loop misses.
        const int N = 1000;
        var allocated = new List<long>(capacity: N);

        for (int i = 0; i < N; i++)
        {
            using var db = new AppDbContext(_options);
            var allocator = new BacklogSequenceAllocator(db);
            allocated.Add(await allocator.AllocateAsync(BacklogEntityType.Story));
        }

        Assert.Equal(N, allocated.Count);
        for (int i = 0; i < N; i++)
        {
            Assert.Equal(i + 1, allocated[i]);
        }
    }

    [Fact]
    public async Task AllEntityTypes_AreIndependent_UnderInterleaving()
    {
        // Interleave all four backlog types and assert each gets its
        // own strictly-monotonic counter without leakage.
        var epic = new List<long>();
        var feature = new List<long>();
        var story = new List<long>();
        var issue = new List<long>();

        for (int i = 0; i < 10; i++)
        {
            using var db = new AppDbContext(_options);
            var allocator = new BacklogSequenceAllocator(db);
            epic.Add(await allocator.AllocateAsync(BacklogEntityType.Epic));
            feature.Add(await allocator.AllocateAsync(BacklogEntityType.Feature));
            story.Add(await allocator.AllocateAsync(BacklogEntityType.Story));
            issue.Add(await allocator.AllocateAsync(BacklogEntityType.Issue));
        }

        var expected = Enumerable.Range(1, 10).Select(i => (long)i).ToArray();
        Assert.Equal(expected, epic);
        Assert.Equal(expected, feature);
        Assert.Equal(expected, story);
        Assert.Equal(expected, issue);
    }

    [Fact]
    public async Task ConcurrentAllocation_StoriesOnly_ProducesUniqueContiguousSequence()
    {
        // 4 workers × 25 allocations = 100 STORY-N values. SQLite
        // serializes writes via its file lock so the concurrency
        // model is "logically concurrent, physically serialized" —
        // proves the allocator never duplicates and never gaps under
        // contention. The Postgres path uses the same atomic
        // UPDATE...RETURNING semantics; this test covers the more
        // restrictive backend.
        const int WorkerCount = 4;
        const int AllocsPerWorker = 25;

        var allWorkers = Enumerable.Range(0, WorkerCount)
            .Select(_ => Task.Run(async () =>
            {
                var results = new List<long>(capacity: AllocsPerWorker);
                for (int j = 0; j < AllocsPerWorker; j++)
                {
                    using var db = new AppDbContext(_options);
                    var allocator = new BacklogSequenceAllocator(db);
                    results.Add(await allocator.AllocateAsync(BacklogEntityType.Story));
                }
                return results;
            }))
            .ToArray();

        var perWorker = await Task.WhenAll(allWorkers);
        var allAllocated = perWorker.SelectMany(r => r).OrderBy(x => x).ToList();

        Assert.Equal(WorkerCount * AllocsPerWorker, allAllocated.Count);
        Assert.Equal(allAllocated.Count, allAllocated.Distinct().Count());
        // No gaps under happy-path concurrency.
        for (int i = 0; i < allAllocated.Count; i++)
        {
            Assert.Equal(i + 1, allAllocated[i]);
        }
    }

    [Fact]
    public async Task ConcurrentAllocation_MixedTypes_PreservesPerTypeUniqueness()
    {
        // 4 workers each allocating one of each type concurrently.
        // The allocator must give per-type contiguous sequences even
        // when callers race across types. This is the "mixed test"
        // AH7 calls out — exercised explicitly because it would
        // otherwise be silent if the shared cache happened to
        // serialize writes globally.
        const int WorkerCount = 8;

        var allWorkers = Enumerable.Range(0, WorkerCount)
            .Select(_ => Task.Run(async () =>
            {
                using var db = new AppDbContext(_options);
                var allocator = new BacklogSequenceAllocator(db);
                var epicSeq = await allocator.AllocateAsync(BacklogEntityType.Epic);
                var storySeq = await allocator.AllocateAsync(BacklogEntityType.Story);
                return (epicSeq, storySeq);
            }))
            .ToArray();

        var pairs = await Task.WhenAll(allWorkers);
        var epicSeqs = pairs.Select(p => p.epicSeq).ToList();
        var storySeqs = pairs.Select(p => p.storySeq).ToList();

        // Each type's allocations are individually unique and
        // contiguous (1..N), regardless of interleaving.
        Assert.Equal(WorkerCount, epicSeqs.Distinct().Count());
        Assert.Equal(WorkerCount, storySeqs.Distinct().Count());
        Assert.Equal(
            Enumerable.Range(1, WorkerCount).Select(i => (long)i).OrderBy(x => x).ToList(),
            epicSeqs.OrderBy(x => x).ToList());
        Assert.Equal(
            Enumerable.Range(1, WorkerCount).Select(i => (long)i).OrderBy(x => x).ToList(),
            storySeqs.OrderBy(x => x).ToList());
    }
}
