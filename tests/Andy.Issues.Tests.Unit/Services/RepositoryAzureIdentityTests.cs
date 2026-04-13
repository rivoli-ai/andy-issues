// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class RepositoryAzureIdentityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public RepositoryAzureIdentityTests()
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

    private RepositoryService NewService(AppDbContext ctx) =>
        new(ctx,
            new RepositoryAccessGuard(ctx),
            new UserDirectoryService(ctx),
            new StubGitHubClient(),
            new StubAzureDevOpsClient(),
            new StubCodeIndexClient(),
            new StubSecretStore(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RepositoryService>.Instance);

    private async Task<Guid> SeedAsync()
    {
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "r",
            CloneUrl = "https://example.com/r.git"
        };
        ctx.Repositories.Add(repo);
        await ctx.SaveChangesAsync();
        return repo.Id;
    }

    [Fact]
    public async Task SetAzureIdentity_Owner_StoresAllFields()
    {
        var id = await SeedAsync();
        await using var ctx = NewContext();
        var result = await NewService(ctx)
            .SetAzureIdentityAsync(id, "cid", "secret", "tenant", "sub", "alice");
        Assert.Equal(SetAzureIdentityResult.Updated, result);

        var repo = await ctx.Repositories.FindAsync(id);
        Assert.True(repo!.HasAzureIdentity);
        Assert.Equal("cid", repo.AzureClientId);
        Assert.Equal("tenant", repo.AzureTenantId);
        Assert.Equal("sub", repo.AzureSubscriptionId);
    }

    [Fact]
    public async Task SetAzureIdentity_NonOwner_Rejected()
    {
        var id = await SeedAsync();
        await using var ctx = NewContext();
        var result = await NewService(ctx)
            .SetAzureIdentityAsync(id, "cid", "secret", "tenant", null, "bob");
        Assert.Equal(SetAzureIdentityResult.NotOwner, result);
    }

    [Fact]
    public async Task SetAzureIdentity_DtoMasksSecret()
    {
        var id = await SeedAsync();
        await using (var ctx = NewContext())
        {
            await NewService(ctx).SetAzureIdentityAsync(id, "cid", "VERY-SECRET", "tenant", "sub", "alice");
        }

        await using (var ctx = NewContext())
        {
            var repo = await ctx.Repositories.FirstAsync(r => r.Id == id);
            var dto = repo.ToDto();
            Assert.True(dto.HasAzureIdentity);
            // RepositoryDto has no secret fields at all — asserted in SecretMaskingTests.
        }
    }

    [Fact]
    public async Task Verify_Owner_WithIdentity_ReturnsSuccess()
    {
        var id = await SeedAsync();
        await using (var ctx = NewContext())
        {
            await NewService(ctx).SetAzureIdentityAsync(id, "cid", "s", "t", null, "alice");
        }

        await using var ctx2 = NewContext();
        var result = await NewService(ctx2).VerifyAzureIdentityAsync(id, "alice");
        Assert.NotNull(result);
        Assert.True(result!.Success);
    }

    [Fact]
    public async Task Verify_Owner_WithoutIdentity_ReturnsFailure()
    {
        var id = await SeedAsync();
        await using var ctx = NewContext();
        var result = await NewService(ctx).VerifyAzureIdentityAsync(id, "alice");
        Assert.NotNull(result);
        Assert.False(result!.Success);
    }

    [Fact]
    public async Task Verify_NonOwner_ReturnsNull()
    {
        var id = await SeedAsync();
        await using var ctx = NewContext();
        var result = await NewService(ctx).VerifyAzureIdentityAsync(id, "bob");
        Assert.Null(result);
    }
}
