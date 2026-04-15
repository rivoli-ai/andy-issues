// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

// Verifies that RepositoryService writes the correct
// andy.issues.events.repository.* outbox row for registration and
// for successful syncs (Story 15.4). Unsuccessful syncs do NOT emit.
public class RepositoryServiceRepoEventsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public RepositoryServiceRepoEventsTests()
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

    private RepositoryService NewService(
        AppDbContext ctx,
        StubGitHubClient? gh = null,
        StubAzureDevOpsClient? az = null)
    {
        var guard = new RepositoryAccessGuard(ctx);
        var dir = new UserDirectoryService(ctx);
        return new RepositoryService(
            ctx, guard, dir,
            gh ?? new StubGitHubClient(),
            az ?? new StubAzureDevOpsClient(),
            new StubCodeIndexClient(),
            new StubSecretStore(),
            NullLogger<RepositoryService>.Instance);
    }

    [Fact]
    public async Task CreateAsync_EmitsRegisteredEvent()
    {
        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).CreateAsync(
                new CreateRepositoryRequest("my-repo", null, "github", "https://github.com/me/my-repo.git", null, null),
                "alice");
            Assert.Equal(CreateRepositoryResult.Created, result.Result);
        }

        await using var verify = NewContext();
        var entry = await verify.Outbox.SingleAsync();
        Assert.EndsWith(".registered", entry.Subject);
        Assert.StartsWith("andy.issues.events.repository.", entry.Subject);

        using var doc = JsonDocument.Parse(entry.PayloadJson);
        var root = doc.RootElement;
        Assert.Equal("my-repo", root.GetProperty("name").GetString());
        Assert.Equal("GitHub", root.GetProperty("provider").GetString());
        Assert.Equal("https://github.com/me/my-repo.git", root.GetProperty("clone_url").GetString());
        Assert.Equal(RepositoryRegisteredPayload.SchemaVersion, root.GetProperty("schema_version").GetInt32());
    }

    [Fact]
    public async Task CreateAsync_AlreadyExists_DoesNotEmitEvent()
    {
        await using (var ctx = NewContext())
        {
            await NewService(ctx).CreateAsync(
                new CreateRepositoryRequest("dup", null, "github", "https://github.com/me/dup.git", null, null),
                "alice");
        }
        var outboxCountAfterCreate = await CountOutbox();

        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).CreateAsync(
                new CreateRepositoryRequest("dup", null, "github", "https://github.com/me/dup.git", null, null),
                "alice");
            Assert.Equal(CreateRepositoryResult.AlreadyExists, result.Result);
        }

        Assert.Equal(outboxCountAfterCreate, await CountOutbox());
    }

    [Fact]
    public async Task SyncFromGitHub_EmitsSyncedEventPerTouchedRepo()
    {
        await SeedGitHubProviderAsync();

        var gh = new StubGitHubClient();
        gh.Returns("owner/one", new GitHubRepositoryInfo(
            ExternalId: "gh-1", Name: "one", FullName: "owner/one", Description: null,
            CloneUrl: "https://github.com/owner/one.git", DefaultBranch: "main"));
        gh.Returns("owner/two", new GitHubRepositoryInfo(
            ExternalId: "gh-2", Name: "two", FullName: "owner/two", Description: null,
            CloneUrl: "https://github.com/owner/two.git", DefaultBranch: "main"));

        SyncResult? syncResult;
        await using (var ctx = NewContext())
        {
            syncResult = await NewService(ctx, gh: gh).SyncFromGitHubAsync("alice", new[] { "owner/one", "owner/two" });
        }
        Assert.NotNull(syncResult);
        Assert.Equal(2, syncResult!.Added);

        await using var verify = NewContext();
        var syncedEntries = await verify.Outbox.Where(e => e.Subject.EndsWith(".synced")).ToListAsync();
        Assert.Equal(2, syncedEntries.Count);

        foreach (var entry in syncedEntries)
        {
            using var doc = JsonDocument.Parse(entry.PayloadJson);
            var root = doc.RootElement;
            Assert.Equal("GitHub", root.GetProperty("provider").GetString());
            Assert.Equal(2, root.GetProperty("added").GetInt32());
            Assert.Equal(0, root.GetProperty("updated").GetInt32());
            Assert.Equal(0, root.GetProperty("error_count").GetInt32());
        }
    }

    [Fact]
    public async Task SyncFromGitHub_SkippedRepo_DoesNotEmitSyncedEvent()
    {
        await SeedGitHubProviderAsync();

        // Seed a pre-existing repo so the sync sees "no change" → skipped.
        await using (var ctx = NewContext())
        {
            ctx.Repositories.Add(new Repository
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "alice",
                Name = "preexisting",
                Provider = RepositoryProvider.GitHub,
                CloneUrl = "https://github.com/o/p.git",
                DefaultBranch = "main",
                ExternalId = "gh-same"
            });
            await ctx.SaveChangesAsync();
        }

        var gh = new StubGitHubClient();
        gh.Returns("o/p", new GitHubRepositoryInfo(
            ExternalId: "gh-same", Name: "preexisting", FullName: "o/p", Description: null,
            CloneUrl: "https://github.com/o/p.git", DefaultBranch: "main"));

        await using (var ctx = NewContext())
        {
            var r = await NewService(ctx, gh: gh).SyncFromGitHubAsync("alice", new[] { "o/p" });
            Assert.Equal(1, r!.Skipped);
        }

        await using var verify = NewContext();
        var syncedEntries = await verify.Outbox.Where(e => e.Subject.EndsWith(".synced")).ToListAsync();
        Assert.Empty(syncedEntries);
    }

    [Fact]
    public async Task SyncFromGitHub_FailedLookup_DoesNotEmitSyncedEvent()
    {
        await SeedGitHubProviderAsync();

        var gh = new StubGitHubClient();
        gh.Returns("bad/repo", null);  // returns "not found"

        await using (var ctx = NewContext())
        {
            var r = await NewService(ctx, gh: gh).SyncFromGitHubAsync("alice", new[] { "bad/repo" });
            Assert.NotEmpty(r!.Errors);
        }

        await using var verify = NewContext();
        var syncedEntries = await verify.Outbox.Where(e => e.Subject.EndsWith(".synced")).ToListAsync();
        Assert.Empty(syncedEntries);
    }

    private async Task SeedGitHubProviderAsync()
    {
        await using var ctx = NewContext();
        ctx.LinkedProviders.Add(new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Provider = LinkedProviderKind.GitHub,
            AccessToken = "fake-token",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<int> CountOutbox()
    {
        await using var ctx = NewContext();
        return await ctx.Outbox.CountAsync();
    }
}
