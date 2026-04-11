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

public class RepositoryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public RepositoryServiceTests()
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
        return new RepositoryService(ctx, guard, dir, gh, az);
    }

    private async Task SeedAsync()
    {
        await using var ctx = NewContext();
        var mine = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "alice-repo",
            CloneUrl = "https://example.com/alice.git"
        };
        var sharedWithMe = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "bob",
            Name = "bob-repo",
            CloneUrl = "https://example.com/bob.git"
        };
        sharedWithMe.AddShare("alice", "bob");
        var unrelated = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "carol",
            Name = "carol-repo",
            CloneUrl = "https://example.com/carol.git"
        };
        ctx.Repositories.AddRange(mine, sharedWithMe, unrelated);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task List_ScopeMine_ReturnsOnlyOwnedRepos()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var result = await service.ListAsync("alice", RepositoryScope.Mine, 1, 20);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("alice-repo", result.Items[0].Name);
    }

    [Fact]
    public async Task List_ScopeShared_ExcludesOwnedRepos()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var result = await service.ListAsync("alice", RepositoryScope.Shared, 1, 20);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("bob-repo", result.Items[0].Name);
    }

    [Fact]
    public async Task List_ScopeAll_ReturnsOwnedAndShared()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var result = await service.ListAsync("alice", RepositoryScope.All, 1, 20);

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, r => r.Name == "alice-repo");
        Assert.Contains(result.Items, r => r.Name == "bob-repo");
        Assert.DoesNotContain(result.Items, r => r.Name == "carol-repo");
    }

    [Fact]
    public async Task Get_OwnerCanView()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);
        var id = await ctx.Repositories.Where(r => r.Name == "alice-repo").Select(r => r.Id).FirstAsync();

        var dto = await service.GetAsync(id, "alice");
        Assert.NotNull(dto);
    }

    [Fact]
    public async Task Get_SharedUserCanView()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);
        var id = await ctx.Repositories.Where(r => r.Name == "bob-repo").Select(r => r.Id).FirstAsync();

        var dto = await service.GetAsync(id, "alice");
        Assert.NotNull(dto);
    }

    [Fact]
    public async Task Get_UnrelatedUserGetsNull()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);
        var id = await ctx.Repositories.Where(r => r.Name == "carol-repo").Select(r => r.Id).FirstAsync();

        var dto = await service.GetAsync(id, "alice");
        Assert.Null(dto);
    }

    [Fact]
    public async Task Delete_OwnerSucceedsAndCascades()
    {
        Guid repoId;
        Guid epicId;

        await using (var ctx = NewContext())
        {
            var repo = new Repository
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "alice",
                Name = "alice-delete",
                CloneUrl = "https://example.com/a.git"
            };
            var epic = new Epic { Id = Guid.NewGuid(), Title = "E1" };
            repo.Epics.Add(epic);
            ctx.Repositories.Add(repo);
            await ctx.SaveChangesAsync();
            repoId = repo.Id;
            epicId = epic.Id;
        }

        await using (var ctx = NewContext())
        {
            var service = NewService(ctx);
            var ok = await service.DeleteAsync(repoId, "alice");
            Assert.True(ok);
        }

        await using (var ctx = NewContext())
        {
            Assert.Null(await ctx.Repositories.FindAsync(repoId));
            Assert.Null(await ctx.Epics.FindAsync(epicId));
        }
    }

    [Fact]
    public async Task Delete_NonOwnerReturnsFalse()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var id = await ctx.Repositories.Where(r => r.Name == "bob-repo").Select(r => r.Id).FirstAsync();
        var service = NewService(ctx);

        var ok = await service.DeleteAsync(id, "alice");
        Assert.False(ok);
    }

    [Fact]
    public async Task AccessGuard_TruthTable()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var guard = new RepositoryAccessGuard(ctx);

        var aliceRepo = await ctx.Repositories.Where(r => r.Name == "alice-repo").Select(r => r.Id).FirstAsync();
        var bobRepo = await ctx.Repositories.Where(r => r.Name == "bob-repo").Select(r => r.Id).FirstAsync();
        var carolRepo = await ctx.Repositories.Where(r => r.Name == "carol-repo").Select(r => r.Id).FirstAsync();

        Assert.True(await guard.CanViewAsync(aliceRepo, "alice"));
        Assert.True(await guard.IsOwnerAsync(aliceRepo, "alice"));

        Assert.True(await guard.CanViewAsync(bobRepo, "alice"));
        Assert.False(await guard.IsOwnerAsync(bobRepo, "alice"));

        Assert.False(await guard.CanViewAsync(carolRepo, "alice"));
        Assert.False(await guard.IsOwnerAsync(carolRepo, "alice"));
    }
}
