// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class RepositoryGitHubSyncTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public RepositoryGitHubSyncTests()
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

    private RepositoryService NewService(AppDbContext ctx, IGitHubClient gh)
    {
        var guard = new RepositoryAccessGuard(ctx);
        var dir = new UserDirectoryService(ctx);
        return new RepositoryService(ctx, guard, dir, gh);
    }

    private async Task SeedLinkedProviderAsync(string userId, string token = "ghp_token")
    {
        await using var ctx = NewContext();
        ctx.LinkedProviders.Add(new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userId,
            Provider = LinkedProviderKind.GitHub,
            AccessToken = token
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Sync_NoLinkedProvider_ReturnsNull()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx, new StubGitHubClient());
        var result = await service.SyncFromGitHubAsync("alice", new[] { "acme/repo1" });
        Assert.Null(result);
    }

    [Fact]
    public async Task Sync_AddsNewRepositories()
    {
        await SeedLinkedProviderAsync("alice");
        var gh = new StubGitHubClient()
            .Returns("acme/r1", new GitHubRepositoryInfo("101", "r1", "acme/r1", "desc", "https://github.com/acme/r1.git", "main"))
            .Returns("acme/r2", new GitHubRepositoryInfo("102", "r2", "acme/r2", null, "https://github.com/acme/r2.git", "master"));

        await using var ctx = NewContext();
        var service = NewService(ctx, gh);
        var result = await service.SyncFromGitHubAsync("alice", new[] { "acme/r1", "acme/r2" });

        Assert.NotNull(result);
        Assert.Equal(2, result!.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Skipped);
        Assert.Empty(result.Errors);

        Assert.Equal(2, await ctx.Repositories.CountAsync(r => r.OwnerUserId == "alice"));
    }

    [Fact]
    public async Task Sync_UpdatesExistingRepositoriesByExternalId()
    {
        await SeedLinkedProviderAsync("alice");
        await using (var ctx = NewContext())
        {
            ctx.Repositories.Add(new Repository
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "alice",
                Name = "stale-name",
                Provider = RepositoryProvider.GitHub,
                CloneUrl = "https://github.com/acme/r1.git",
                DefaultBranch = "main",
                ExternalId = "101"
            });
            await ctx.SaveChangesAsync();
        }

        var gh = new StubGitHubClient()
            .Returns("acme/r1", new GitHubRepositoryInfo("101", "fresh-name", "acme/r1", "new desc", "https://github.com/acme/r1.git", "main"));

        await using (var ctx = NewContext())
        {
            var service = NewService(ctx, gh);
            var result = await service.SyncFromGitHubAsync("alice", new[] { "acme/r1" });

            Assert.Equal(0, result!.Added);
            Assert.Equal(1, result.Updated);
        }

        await using (var ctx = NewContext())
        {
            var repo = await ctx.Repositories.FirstAsync(r => r.ExternalId == "101");
            Assert.Equal("fresh-name", repo.Name);
            Assert.Equal("new desc", repo.Description);
        }
    }

    [Fact]
    public async Task Sync_SkipsUnchangedAndReportsErrors()
    {
        await SeedLinkedProviderAsync("alice");
        await using (var ctx = NewContext())
        {
            ctx.Repositories.Add(new Repository
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "alice",
                Name = "r1",
                Description = "same",
                Provider = RepositoryProvider.GitHub,
                CloneUrl = "https://github.com/acme/r1.git",
                DefaultBranch = "main",
                ExternalId = "101"
            });
            await ctx.SaveChangesAsync();
        }

        var gh = new StubGitHubClient()
            .Returns("acme/r1", new GitHubRepositoryInfo("101", "r1", "acme/r1", "same", "https://github.com/acme/r1.git", "main"))
            .Returns("acme/missing", null);

        await using var ctx2 = NewContext();
        var service = NewService(ctx2, gh);
        var result = await service.SyncFromGitHubAsync("alice", new[] { "acme/r1", "acme/missing", "" });

        Assert.Equal(0, result!.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(2, result.Skipped); // r1 unchanged + empty string
        Assert.Single(result.Errors);
        Assert.Contains("acme/missing", result.Errors[0]);
    }
}
