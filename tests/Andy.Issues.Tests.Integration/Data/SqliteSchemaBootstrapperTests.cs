// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Integration.Data;

/// <summary>
/// Regression tests for the embedded SQLite schema heal.
///
/// Conductor users who installed andy-issues before commit 3f414ef hit
/// a `SQLite Error 1: 'no such column: u.ContentHash'` on every Story
/// query. The reason: their DB was created by EnsureCreatedAsync at an
/// older model snapshot. When a new property landed on UserStory,
/// EnsureCreated did nothing (no-op on existing tables) and the SQLite
/// path doesn't run migrations, so the column never got added.
///
/// SqliteSchemaBootstrapper.HealMissingColumnsAsync closes the gap by
/// diffing the live EF model against the actual schema. These tests
/// pin its behaviour against the exact failure mode and against the
/// fresh-DB happy path.
/// </summary>
public class SqliteSchemaBootstrapperTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SqliteSchemaBootstrapperTests()
    {
        // Shared :memory: connection so multiple contexts see the same DB.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext NewContext() => new(_options);

    /// <summary>
    /// Reproduces the production failure: a DB created by EnsureCreated
    /// at an older model snapshot is missing a column the current model
    /// expects. Without the heal, every query touching that column 500s.
    /// </summary>
    [Fact]
    public async Task Heal_AddsMissingColumn_QueryWorksAfterwards()
    {
        // ARRANGE: create the live schema, then strip the ContentHash
        // column to simulate the older-binary state.
        await using (var ctx = NewContext())
        {
            await ctx.Database.EnsureCreatedAsync();
        }
        await ExecuteAsync(@"ALTER TABLE ""UserStories"" DROP COLUMN ""ContentHash"";");

        // Sanity: the column is gone and the query that 500s in
        // production fails locally too.
        await using (var ctx = NewContext())
        {
            await Assert.ThrowsAsync<SqliteException>(async () =>
            {
                await ctx.UserStories.Select(s => s.ContentHash).ToListAsync();
            });
        }

        // ACT: heal the schema.
        await using (var ctx = NewContext())
        {
            var healed = await SqliteSchemaBootstrapper.HealMissingColumnsAsync(
                ctx,
                NullLogger.Instance);
            Assert.True(healed >= 1, "expected at least the ContentHash column to be healed");
        }

        // ASSERT: the ContentHash query now succeeds, and a round-trip
        // through SaveChanges (which recomputes ContentHash) persists.
        var storyId = await SeedRepoEpicFeatureStoryAsync();
        await using (var ctx = NewContext())
        {
            var story = await ctx.UserStories.FirstOrDefaultAsync(s => s.Id == storyId);
            Assert.NotNull(story);
            Assert.False(string.IsNullOrEmpty(story!.ContentHash));
        }
    }

    /// <summary>
    /// On a fresh DB the heal is a no-op because EnsureCreated has
    /// already produced the full schema from the live model.
    /// </summary>
    [Fact]
    public async Task Heal_OnFreshDb_IsNoOp()
    {
        await using (var ctx = NewContext())
        {
            await ctx.Database.EnsureCreatedAsync();
            var healed = await SqliteSchemaBootstrapper.HealMissingColumnsAsync(
                ctx,
                NullLogger.Instance);
            Assert.Equal(0, healed);
        }
    }

    /// <summary>
    /// On a completely empty DB (no tables yet) the heal must not crash —
    /// it sees every table as "not created" and leaves them alone for
    /// EnsureCreated to handle.
    /// </summary>
    [Fact]
    public async Task Heal_OnEmptyDb_IsNoOp()
    {
        await using var ctx = NewContext();
        var healed = await SqliteSchemaBootstrapper.HealMissingColumnsAsync(
            ctx,
            NullLogger.Instance);
        Assert.Equal(0, healed);
    }

    /// <summary>
    /// Re-running the heal is idempotent: a second run after the first
    /// added the column must find nothing to do.
    /// </summary>
    [Fact]
    public async Task Heal_IsIdempotent()
    {
        await using (var ctx = NewContext())
        {
            await ctx.Database.EnsureCreatedAsync();
        }
        await ExecuteAsync(@"ALTER TABLE ""UserStories"" DROP COLUMN ""ContentHash"";");

        int firstHeal;
        await using (var ctx = NewContext())
        {
            firstHeal = await SqliteSchemaBootstrapper.HealMissingColumnsAsync(
                ctx,
                NullLogger.Instance);
        }
        Assert.True(firstHeal >= 1);

        await using (var ctx = NewContext())
        {
            var secondHeal = await SqliteSchemaBootstrapper.HealMissingColumnsAsync(
                ctx,
                NullLogger.Instance);
            Assert.Equal(0, secondHeal);
        }
    }

    /// <summary>
    /// Reproduces the production failure that surfaced after #184 shipped
    /// `AcceptanceCriteriaList`, `Risks`, `TestPlan` on `UserStory` —
    /// `List&lt;string&gt;` columns backed by a JSON value converter and
    /// non-nullable on the model. The original heal refused to add them
    /// because they have no SQL default. The converter materialises
    /// null → empty list, so we can safely add them as nullable TEXT
    /// on the SQLite path and let the converter close the gap.
    /// </summary>
    [Fact]
    public async Task Heal_AddsConverterBackedListColumns_QueryWorksAfterwards()
    {
        // ARRANGE: create the live schema, then strip the three list
        // columns to simulate a DB created before #184 shipped.
        await using (var ctx = NewContext())
        {
            await ctx.Database.EnsureCreatedAsync();
        }
        await ExecuteAsync(@"ALTER TABLE ""UserStories"" DROP COLUMN ""AcceptanceCriteriaList"";");
        await ExecuteAsync(@"ALTER TABLE ""UserStories"" DROP COLUMN ""Risks"";");
        await ExecuteAsync(@"ALTER TABLE ""UserStories"" DROP COLUMN ""TestPlan"";");

        // Sanity: the columns are gone and the query that 500s in
        // production fails locally too.
        await using (var ctx = NewContext())
        {
            await Assert.ThrowsAsync<SqliteException>(async () =>
            {
                await ctx.UserStories.Select(s => s.AcceptanceCriteriaList).ToListAsync();
            });
        }

        // ACT: heal the schema.
        await using (var ctx = NewContext())
        {
            var healed = await SqliteSchemaBootstrapper.HealMissingColumnsAsync(
                ctx,
                NullLogger.Instance);
            Assert.True(healed >= 3, "expected at least the three list columns to be healed");
        }

        // ASSERT: the list queries now succeed and round-trip cleanly
        // through SaveChanges. Existing rows (added pre-#184) get null
        // on disk and the converter materialises them as empty lists.
        var storyId = await SeedRepoEpicFeatureStoryAsync();
        await using (var ctx = NewContext())
        {
            var story = await ctx.UserStories.FirstOrDefaultAsync(s => s.Id == storyId);
            Assert.NotNull(story);
            Assert.NotNull(story!.AcceptanceCriteriaList);
            Assert.Empty(story.AcceptanceCriteriaList);
            Assert.NotNull(story.Risks);
            Assert.Empty(story.Risks);
            Assert.NotNull(story.TestPlan);
            Assert.Empty(story.TestPlan);
        }
    }

    /// <summary>
    /// Reproduces the whole-missing-TABLE blind spot (the failure mode
    /// that bit andy-docs' DocumentEmbeddings table): a DB created by an
    /// older binary lacks an entire table the current model declares.
    /// EnsureCreated no-ops (other tables exist) and the column heal
    /// skips the table (zero live columns), so every query against it
    /// fails with "no such table" forever. HealAsync must recreate the
    /// table — with all model columns AND its indexes — using
    /// EnsureCreated's own generated DDL.
    /// </summary>
    [Fact]
    public async Task HealAsync_RecreatesMissingTable_WithColumnsAndIndexes_RoundTrips()
    {
        // ARRANGE: create the live schema, then drop a leaf table
        // (AuditLog — nothing references it) to simulate the
        // older-binary state.
        await using (var ctx = NewContext())
        {
            await ctx.Database.EnsureCreatedAsync();
        }
        await ExecuteAsync(@"DROP TABLE ""AuditLog"";");

        // Sanity: the table is gone and querying it fails.
        await using (var ctx = NewContext())
        {
            await Assert.ThrowsAsync<SqliteException>(async () =>
            {
                await ctx.AuditLog.ToListAsync();
            });
        }

        // ACT: heal the schema.
        await using (var ctx = NewContext())
        {
            var healed = await SqliteSchemaBootstrapper.HealAsync(
                ctx,
                NullLogger.Instance);
            Assert.True(healed >= 1, "expected at least the AuditLog table to be healed");
        }

        // ASSERT: all model columns are present…
        var columns = await ReadColumnNamesAsync("AuditLog");
        Assert.Superset(
            new HashSet<string>
            {
                "Id", "UserId", "Action", "ResourceType", "ResourceId", "Details", "CreatedAt"
            },
            columns);

        // …the table's indexes were recreated…
        var indexes = await ReadIndexNamesAsync("AuditLog");
        Assert.Contains("IX_AuditLog_ResourceType_ResourceId_CreatedAt", indexes);
        Assert.Contains("IX_AuditLog_UserId", indexes);

        // …and an INSERT/SELECT round-trip works through EF.
        var entryId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.AuditLog.Add(new AuditLogEntry
            {
                Id = entryId,
                UserId = "user-1",
                Action = "agent-rules.edit",
                ResourceType = "Repository",
                ResourceId = "repo-1",
                Details = "healed-table round-trip"
            });
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = NewContext())
        {
            var entry = await ctx.AuditLog.FirstOrDefaultAsync(e => e.Id == entryId);
            Assert.NotNull(entry);
            Assert.Equal("agent-rules.edit", entry!.Action);
            Assert.Equal("healed-table round-trip", entry.Details);
        }
    }

    /// <summary>
    /// On a completely empty DB (zero user tables) HealAsync must be a
    /// no-op: materialising the full schema is EnsureCreated's job, and
    /// the heal must not race it by creating tables piecemeal.
    /// </summary>
    [Fact]
    public async Task HealAsync_OnEmptyDb_IsNoOp()
    {
        await using (var ctx = NewContext())
        {
            var healed = await SqliteSchemaBootstrapper.HealAsync(
                ctx,
                NullLogger.Instance);
            Assert.Equal(0, healed);
        }

        // The DB must still be empty — the heal must not have created
        // any table behind EnsureCreated's back.
        using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
        var tableCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        Assert.Equal(0, tableCount);
    }

    /// <summary>
    /// On a fresh, fully-created DB HealAsync (tables + columns) is a
    /// complete no-op, and re-running it after a table heal finds
    /// nothing left to do.
    /// </summary>
    [Fact]
    public async Task HealAsync_IsIdempotent()
    {
        await using (var ctx = NewContext())
        {
            await ctx.Database.EnsureCreatedAsync();
            var healedFresh = await SqliteSchemaBootstrapper.HealAsync(
                ctx,
                NullLogger.Instance);
            Assert.Equal(0, healedFresh);
        }

        await ExecuteAsync(@"DROP TABLE ""AuditLog"";");

        await using (var ctx = NewContext())
        {
            var firstHeal = await SqliteSchemaBootstrapper.HealAsync(
                ctx,
                NullLogger.Instance);
            Assert.True(firstHeal >= 1);
        }
        await using (var ctx = NewContext())
        {
            var secondHeal = await SqliteSchemaBootstrapper.HealAsync(
                ctx,
                NullLogger.Instance);
            Assert.Equal(0, secondHeal);
        }
    }

    private async Task<HashSet<string>> ReadColumnNamesAsync(string tableName)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }
        return columns;
    }

    private async Task<HashSet<string>> ReadIndexNamesAsync(string tableName)
    {
        var indexes = new HashSet<string>(StringComparer.Ordinal);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA index_list(\"{tableName}\");";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // PRAGMA index_list columns: seq (0), name (1), unique (2), ...
            indexes.Add(reader.GetString(1));
        }
        return indexes;
    }

    private async Task<Guid> SeedRepoEpicFeatureStoryAsync()
    {
        var repoId = Guid.NewGuid();
        var epicId = Guid.NewGuid();
        var featureId = Guid.NewGuid();
        var storyId = Guid.NewGuid();
        await using var ctx = NewContext();
        ctx.Repositories.Add(new Repository
        {
            Id = repoId,
            OwnerUserId = "owner",
            Name = "demo",
            Provider = RepositoryProvider.GitHub,
            CloneUrl = "https://example.com/demo.git",
            DefaultBranch = "main"
        });
        ctx.Epics.Add(new Epic
        {
            Id = epicId,
            RepositoryId = repoId,
            Title = "Epic A",
            Order = 1
        });
        ctx.Features.Add(new Feature
        {
            Id = featureId,
            EpicId = epicId,
            Title = "Feature A",
            Order = 1
        });
        ctx.UserStories.Add(new UserStory
        {
            Id = storyId,
            FeatureId = featureId,
            Title = "Story A",
            Description = "Body",
            Order = 1
        });
        await ctx.SaveChangesAsync();
        return storyId;
    }

    private async Task ExecuteAsync(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
