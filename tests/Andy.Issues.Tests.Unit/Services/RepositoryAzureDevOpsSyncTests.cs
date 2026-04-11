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

public class RepositoryAzureDevOpsSyncTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public RepositoryAzureDevOpsSyncTests()
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

    private RepositoryService NewService(AppDbContext ctx, IAzureDevOpsClient az)
    {
        var guard = new RepositoryAccessGuard(ctx);
        var dir = new UserDirectoryService(ctx);
        var gh = new StubGitHubClient();
        return new RepositoryService(ctx, guard, dir, gh, az);
    }

    private async Task SeedLinkedProviderAsync(string userId)
    {
        await using var ctx = NewContext();
        ctx.LinkedProviders.Add(new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userId,
            Provider = LinkedProviderKind.AzureDevOps,
            AccessToken = "pat-value"
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Sync_NoLinkedProvider_ReturnsNull()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx, new StubAzureDevOpsClient());
        var result = await service.SyncFromAzureDevOpsAsync("alice", "contoso", "proj", new[] { "id1" });
        Assert.Null(result);
    }

    [Fact]
    public async Task Sync_MissingOrgOrProject_ReturnsError()
    {
        await SeedLinkedProviderAsync("alice");
        await using var ctx = NewContext();
        var service = NewService(ctx, new StubAzureDevOpsClient());
        var result = await service.SyncFromAzureDevOpsAsync("alice", "", "proj", new[] { "id1" });
        Assert.NotNull(result);
        Assert.Single(result!.Errors);
    }

    [Fact]
    public async Task Sync_AddsNewAndUpdatesExistingByExternalId()
    {
        await SeedLinkedProviderAsync("alice");
        var az = new StubAzureDevOpsClient()
            .Returns("contoso", "proj", "repo-a",
                new AzureDevOpsRepositoryInfo("guid-a", "repo-a", "desc", "https://dev.azure.com/contoso/proj/_git/repo-a", "main", "proj", "contoso"))
            .Returns("contoso", "proj", "repo-b",
                new AzureDevOpsRepositoryInfo("guid-b", "repo-b", null, "https://dev.azure.com/contoso/proj/_git/repo-b", "master", "proj", "contoso"));

        await using (var ctx = NewContext())
        {
            ctx.Repositories.Add(new Repository
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "alice",
                Name = "outdated",
                Provider = RepositoryProvider.AzureDevOps,
                CloneUrl = "https://old.url",
                DefaultBranch = "trunk",
                ExternalId = "guid-a"
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            var service = NewService(ctx, az);
            var result = await service.SyncFromAzureDevOpsAsync(
                "alice", "contoso", "proj", new[] { "repo-a", "repo-b" });

            Assert.Equal(1, result!.Added);
            Assert.Equal(1, result.Updated);
            Assert.Empty(result.Errors);
        }

        await using (var ctx = NewContext())
        {
            var a = await ctx.Repositories.FirstAsync(r => r.ExternalId == "guid-a");
            Assert.Equal("repo-a", a.Name);
            Assert.Equal("main", a.DefaultBranch);

            Assert.Contains(ctx.Repositories, r => r.ExternalId == "guid-b");
        }
    }

    [Fact]
    public async Task Sync_NotFoundProducesError()
    {
        await SeedLinkedProviderAsync("alice");
        var az = new StubAzureDevOpsClient()
            .Returns("contoso", "proj", "missing", null);

        await using var ctx = NewContext();
        var service = NewService(ctx, az);
        var result = await service.SyncFromAzureDevOpsAsync(
            "alice", "contoso", "proj", new[] { "missing" });

        Assert.Equal(0, result!.Added);
        Assert.Single(result.Errors);
        Assert.Contains("missing", result.Errors[0]);
    }
}
