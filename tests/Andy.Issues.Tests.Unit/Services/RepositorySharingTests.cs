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

public class RepositorySharingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public RepositorySharingTests()
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

    private RepositoryService NewService(AppDbContext ctx)
    {
        var guard = new RepositoryAccessGuard(ctx);
        var dir = new UserDirectoryService(ctx);
        var gh = new StubGitHubClient();
        var az = new StubAzureDevOpsClient();
        var ci = new StubCodeIndexClient();
        var ss = new StubSecretStore();
        return new RepositoryService(ctx, guard, dir, gh, az, ci, ss,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RepositoryService>.Instance);
    }

    private async Task<Guid> SeedAsync()
    {
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "alice-repo",
            CloneUrl = "https://example.com/alice.git"
        };
        ctx.Repositories.Add(repo);

        ctx.UserDirectory.AddRange(
            new UserDirectoryEntry { Id = Guid.NewGuid(), UserId = "alice", Email = "alice@example.com" },
            new UserDirectoryEntry { Id = Guid.NewGuid(), UserId = "bob", Email = "bob@example.com" });

        await ctx.SaveChangesAsync();
        return repo.Id;
    }

    [Fact]
    public async Task Share_ValidEmail_Creates()
    {
        var id = await SeedAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, dto) = await service.ShareAsync(id, "bob@example.com", "alice");

        Assert.Equal(ShareResult.Created, result);
        Assert.NotNull(dto);
        Assert.Equal("bob", dto!.SharedWithUserId);
    }

    [Fact]
    public async Task Share_DuplicateReturnsAlreadyShared()
    {
        var id = await SeedAsync();

        await using (var ctx = NewContext())
        {
            var service = NewService(ctx);
            await service.ShareAsync(id, "bob@example.com", "alice");
        }

        await using var ctx2 = NewContext();
        var service2 = NewService(ctx2);
        var (result, dto) = await service2.ShareAsync(id, "bob@example.com", "alice");

        Assert.Equal(ShareResult.AlreadyShared, result);
        Assert.NotNull(dto);
    }

    [Fact]
    public async Task Share_SelfEmailRejected()
    {
        var id = await SeedAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, dto) = await service.ShareAsync(id, "alice@example.com", "alice");

        Assert.Equal(ShareResult.SelfShareRejected, result);
        Assert.Null(dto);
    }

    [Fact]
    public async Task Share_UnknownEmailReturnsEmailNotFound()
    {
        var id = await SeedAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, _) = await service.ShareAsync(id, "ghost@example.com", "alice");

        Assert.Equal(ShareResult.EmailNotFound, result);
    }

    [Fact]
    public async Task Share_NonOwnerReturnsNotOwner()
    {
        var id = await SeedAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, _) = await service.ShareAsync(id, "bob@example.com", "bob");

        Assert.Equal(ShareResult.NotOwner, result);
    }

    [Fact]
    public async Task Share_MissingRepositoryReturnsNotFound()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, _) = await service.ShareAsync(Guid.NewGuid(), "bob@example.com", "alice");

        Assert.Equal(ShareResult.NotFound, result);
    }

    [Fact]
    public async Task ListShares_OwnerSeesShares()
    {
        var id = await SeedAsync();
        await using (var ctx = NewContext())
        {
            var service = NewService(ctx);
            await service.ShareAsync(id, "bob@example.com", "alice");
        }

        await using var ctx2 = NewContext();
        var service2 = NewService(ctx2);
        var shares = await service2.ListSharesAsync(id, "alice");

        Assert.NotNull(shares);
        Assert.Single(shares!);
        Assert.Equal("bob", shares![0].SharedWithUserId);
    }

    [Fact]
    public async Task ListShares_NonOwnerGetsNull()
    {
        var id = await SeedAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);
        var shares = await service.ListSharesAsync(id, "bob");
        Assert.Null(shares);
    }

    [Fact]
    public async Task Unshare_RemovesShare()
    {
        var id = await SeedAsync();
        await using (var ctx = NewContext())
        {
            var service = NewService(ctx);
            await service.ShareAsync(id, "bob@example.com", "alice");
        }

        await using (var ctx = NewContext())
        {
            var service = NewService(ctx);
            var ok = await service.UnshareAsync(id, "bob", "alice");
            Assert.True(ok);
        }

        await using (var ctx = NewContext())
        {
            Assert.Empty(ctx.RepositoryShares.Where(s => s.RepositoryId == id));
        }
    }

    [Fact]
    public async Task Unshare_NotExistingReturnsFalse()
    {
        var id = await SeedAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);
        var ok = await service.UnshareAsync(id, "nobody", "alice");
        Assert.False(ok);
    }
}
