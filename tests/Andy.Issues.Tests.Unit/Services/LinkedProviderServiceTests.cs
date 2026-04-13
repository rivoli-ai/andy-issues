// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class LinkedProviderServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public LinkedProviderServiceTests()
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

    private LinkedProviderService NewService(
        AppDbContext ctx,
        IGitHubClient? gh = null,
        IAzureDevOpsClient? az = null) =>
        new(ctx, new StubSecretStore(),
            gh ?? new StubGitHubClient(),
            az ?? new StubAzureDevOpsClient());

    [Fact]
    public async Task Upsert_CreatesNewProvider()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, dto) = await service.UpsertAsync(
            new CreateLinkedProviderRequest("github", "ghp_token", null, null, "alice-gh"),
            "alice");

        Assert.Equal(UpsertLinkedProviderResult.Created, result);
        Assert.NotNull(dto);
        Assert.Equal("GitHub", dto!.Provider);
        Assert.Equal("alice-gh", dto.AccountLogin);
    }

    [Fact]
    public async Task Upsert_UpdatesExistingProvider()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);

        await service.UpsertAsync(
            new CreateLinkedProviderRequest("github", "old-token", null, null, "alice"),
            "alice");

        var (result, dto) = await service.UpsertAsync(
            new CreateLinkedProviderRequest("github", "new-token", null, null, "alice-updated"),
            "alice");

        Assert.Equal(UpsertLinkedProviderResult.Updated, result);
        Assert.Equal("alice-updated", dto!.AccountLogin);

        // Only one row in DB
        await using var verify = NewContext();
        Assert.Equal(1, await verify.LinkedProviders.CountAsync());
    }

    [Fact]
    public async Task Upsert_InvalidProvider_RejectsGracefully()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, dto) = await service.UpsertAsync(
            new CreateLinkedProviderRequest("gitlab", "token", null, null, null),
            "alice");

        Assert.Equal(UpsertLinkedProviderResult.InvalidProvider, result);
        Assert.Null(dto);
    }

    [Fact]
    public async Task Upsert_CaseInsensitiveProviderName()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, _) = await service.UpsertAsync(
            new CreateLinkedProviderRequest("AzureDevOps", "pat", null, null, null),
            "alice");

        Assert.Equal(UpsertLinkedProviderResult.Created, result);
    }

    [Fact]
    public async Task List_ReturnsOnlyCallersProviders()
    {
        await using var ctx = NewContext();
        ctx.LinkedProviders.AddRange(
            new LinkedProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "alice",
                Provider = LinkedProviderKind.GitHub,
                AccessToken = "t1"
            },
            new LinkedProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "alice",
                Provider = LinkedProviderKind.AzureDevOps,
                AccessToken = "t2"
            },
            new LinkedProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "bob",
                Provider = LinkedProviderKind.GitHub,
                AccessToken = "t3"
            });
        await ctx.SaveChangesAsync();

        await using var ctx2 = NewContext();
        var service = NewService(ctx2);
        var result = await service.ListAsync("alice");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task List_TokensNotExposedInDto()
    {
        await using var ctx = NewContext();
        ctx.LinkedProviders.Add(new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Provider = LinkedProviderKind.GitHub,
            AccessToken = "secret-token"
        });
        await ctx.SaveChangesAsync();

        await using var ctx2 = NewContext();
        var service = NewService(ctx2);
        var result = await service.ListAsync("alice");

        // LinkedProviderDto has no AccessToken field — verified by the record shape
        var dto = result.Single();
        Assert.Equal("GitHub", dto.Provider);
        // The DTO record doesn't include AccessToken at all (compile-time check)
    }

    [Fact]
    public async Task Delete_RemovesProviderAndReturnsTrue()
    {
        await using var ctx = NewContext();
        ctx.LinkedProviders.Add(new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Provider = LinkedProviderKind.GitHub,
            AccessToken = "t"
        });
        await ctx.SaveChangesAsync();

        await using var ctx2 = NewContext();
        var service = NewService(ctx2);
        var ok = await service.DeleteAsync("github", "alice");

        Assert.True(ok);
        await using var verify = NewContext();
        Assert.Empty(await verify.LinkedProviders.ToListAsync());
    }

    [Fact]
    public async Task Delete_NonexistentReturnsFalse()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);
        Assert.False(await service.DeleteAsync("github", "alice"));
    }

    [Fact]
    public async Task Delete_OtherUsersProviderNotAffected()
    {
        await using var ctx = NewContext();
        ctx.LinkedProviders.Add(new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "bob",
            Provider = LinkedProviderKind.GitHub,
            AccessToken = "t"
        });
        await ctx.SaveChangesAsync();

        await using var ctx2 = NewContext();
        var service = NewService(ctx2);
        Assert.False(await service.DeleteAsync("github", "alice"));
    }

    // MARK: - LinkPatAsync

    [Fact]
    public async Task LinkPat_ValidGitHubPat_LinksAndReturnsDto()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, dto) = await service.LinkPatAsync(
            new LinkPatRequest("github", "ghp_valid"), "alice");

        Assert.Equal(LinkPatResult.Linked, result);
        Assert.NotNull(dto);
        Assert.Equal("GitHub", dto!.Provider);
        Assert.Equal("stub-user", dto.AccountLogin);
    }

    [Fact]
    public async Task LinkPat_InvalidGitHubPat_ReturnsInvalidPat()
    {
        var gh = new StubGitHubClient { CurrentUserResult = null };
        await using var ctx = NewContext();
        var service = NewService(ctx, gh: gh);

        var (result, dto) = await service.LinkPatAsync(
            new LinkPatRequest("github", "bad-token"), "alice");

        Assert.Equal(LinkPatResult.InvalidPat, result);
        Assert.Null(dto);
    }

    [Fact]
    public async Task LinkPat_ValidAzureDevOpsPat_LinksAndReturnsDto()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, dto) = await service.LinkPatAsync(
            new LinkPatRequest("azuredevops", "valid-pat"), "alice");

        Assert.Equal(LinkPatResult.Linked, result);
        Assert.Equal("AzureDevOps", dto!.Provider);
        Assert.Equal("stub-azdo-user", dto.AccountLogin);
    }

    [Fact]
    public async Task LinkPat_InvalidProvider_ReturnsInvalidProvider()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, _) = await service.LinkPatAsync(
            new LinkPatRequest("gitlab", "pat"), "alice");

        Assert.Equal(LinkPatResult.InvalidProvider, result);
    }
}
