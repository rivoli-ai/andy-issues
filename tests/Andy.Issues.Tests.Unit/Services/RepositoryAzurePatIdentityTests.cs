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

public class RepositoryAzurePatIdentityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubAzureDevOpsClient _azClient = new();

    public RepositoryAzurePatIdentityTests()
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
            _azClient,
            new StubCodeIndexClient(),
            new StubSecretStore(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RepositoryService>.Instance);

    private async Task<Guid> SeedRepoAsync()
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

    // MARK: - Domain kind switching

    [Fact]
    public async Task SetAzurePatIdentity_PersistsColumns_KindBecomesPat()
    {
        var id = await SeedRepoAsync();
        await using var ctx = NewContext();
        var result = await NewService(ctx)
            .SetAzurePatIdentityAsync(id, "contoso", "my-project", "the-pat", "alice");
        Assert.Equal(SetAzureIdentityResult.Updated, result);

        var repo = await ctx.Repositories.FindAsync(id);
        Assert.Equal("contoso", repo!.AzureOrganization);
        Assert.Equal("my-project", repo.AzureProject);
        Assert.Equal("the-pat", repo.AzurePat); // StubSecretStore passes through
        Assert.True(repo.HasPatIdentity);
        Assert.Equal(AzureIdentityKind.Pat, repo.HasAzureIdentityKind);
    }

    [Fact]
    public async Task SetAzurePatIdentity_ClearsServicePrincipalFields()
    {
        var id = await SeedRepoAsync();
        await using (var ctx = NewContext())
        {
            await NewService(ctx).SetAzureIdentityAsync(id, "cid", "secret", "tenant", "sub", "alice");
        }
        await using (var ctx = NewContext())
        {
            await NewService(ctx).SetAzurePatIdentityAsync(id, "org", "proj", "pat", "alice");
        }

        await using var verify = NewContext();
        var repo = await verify.Repositories.FirstAsync(r => r.Id == id);
        Assert.Null(repo.AzureClientId);
        Assert.Null(repo.AzureClientSecret);
        Assert.Null(repo.AzureTenantId);
        Assert.Null(repo.AzureSubscriptionId);
        Assert.Equal(AzureIdentityKind.Pat, repo.HasAzureIdentityKind);
    }

    [Fact]
    public async Task SetAzureIdentity_ServicePrincipal_ClearsPatFields()
    {
        var id = await SeedRepoAsync();
        await using (var ctx = NewContext())
        {
            await NewService(ctx).SetAzurePatIdentityAsync(id, "org", "proj", "pat", "alice");
        }
        await using (var ctx = NewContext())
        {
            await NewService(ctx).SetAzureIdentityAsync(id, "cid", "secret", "tenant", null, "alice");
        }

        await using var verify = NewContext();
        var repo = await verify.Repositories.FirstAsync(r => r.Id == id);
        Assert.Null(repo.AzureOrganization);
        Assert.Null(repo.AzureProject);
        Assert.Null(repo.AzurePat);
        Assert.Equal(AzureIdentityKind.ServicePrincipal, repo.HasAzureIdentityKind);
    }

    [Fact]
    public async Task SetAzurePatIdentity_NonOwnerRejected()
    {
        var id = await SeedRepoAsync();
        await using var ctx = NewContext();
        var result = await NewService(ctx)
            .SetAzurePatIdentityAsync(id, "org", "proj", "pat", "bob");
        Assert.Equal(SetAzureIdentityResult.NotOwner, result);
    }

    [Fact]
    public async Task SetAzurePatIdentity_UnknownRepoReturnsNotFound()
    {
        await using var ctx = NewContext();
        var result = await NewService(ctx)
            .SetAzurePatIdentityAsync(Guid.NewGuid(), "org", "proj", "pat", "alice");
        Assert.Equal(SetAzureIdentityResult.NotFound, result);
    }

    // MARK: - Verify branches

    [Fact]
    public async Task Verify_None_ReturnsNotConfigured()
    {
        var id = await SeedRepoAsync();
        await using var ctx = NewContext();
        var result = await NewService(ctx).VerifyAzureIdentityAsync(id, "alice");
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Contains("No Azure identity", result.Message);
    }

    [Fact]
    public async Task Verify_ServicePrincipal_ReturnsPresencePass()
    {
        var id = await SeedRepoAsync();
        await using (var ctx = NewContext())
        {
            await NewService(ctx).SetAzureIdentityAsync(id, "cid", "s", "t", null, "alice");
        }
        await using var ctx2 = NewContext();
        var result = await NewService(ctx2).VerifyAzureIdentityAsync(id, "alice");
        Assert.True(result!.Success);
        Assert.Contains("service-principal", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Verify_Pat_CallsConnectionDataAndReturnsSuccessOnAuthId()
    {
        var id = await SeedRepoAsync();
        await using (var ctx = NewContext())
        {
            await NewService(ctx).SetAzurePatIdentityAsync(id, "contoso", "proj", "the-pat", "alice");
        }
        _azClient.ConnectionResults["contoso"] = new AzureDevOpsConnectionInfo("uid-42", "Alice Azure");

        await using var ctx2 = NewContext();
        var result = await NewService(ctx2).VerifyAzureIdentityAsync(id, "alice");
        Assert.True(result!.Success);
        Assert.Contains("Alice Azure", result.Message);
    }

    [Fact]
    public async Task Verify_Pat_NullConnection_ReturnsFailure()
    {
        var id = await SeedRepoAsync();
        await using (var ctx = NewContext())
        {
            await NewService(ctx).SetAzurePatIdentityAsync(id, "contoso", "proj", "bad-pat", "alice");
        }
        _azClient.ConnectionResults["contoso"] = null;

        await using var ctx2 = NewContext();
        var result = await NewService(ctx2).VerifyAzureIdentityAsync(id, "alice");
        Assert.False(result!.Success);
        Assert.Contains("contoso", result.Message);
    }

    [Fact]
    public async Task Verify_Pat_EmptyAuthenticatedUserId_ReturnsFailure()
    {
        var id = await SeedRepoAsync();
        await using (var ctx = NewContext())
        {
            await NewService(ctx).SetAzurePatIdentityAsync(id, "contoso", "proj", "pat", "alice");
        }
        _azClient.ConnectionResults["contoso"] = new AzureDevOpsConnectionInfo(string.Empty, "Anonymous");

        await using var ctx2 = NewContext();
        var result = await NewService(ctx2).VerifyAzureIdentityAsync(id, "alice");
        Assert.False(result!.Success);
    }

    [Fact]
    public async Task Verify_NonOwner_ReturnsNull()
    {
        var id = await SeedRepoAsync();
        await using (var ctx = NewContext())
        {
            await NewService(ctx).SetAzurePatIdentityAsync(id, "contoso", "proj", "pat", "alice");
        }
        await using var ctx2 = NewContext();
        Assert.Null(await NewService(ctx2).VerifyAzureIdentityAsync(id, "bob"));
    }
}
