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

public class RepositoryLlmOverrideTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public RepositoryLlmOverrideTests()
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
            new StubAzureDevOpsClient());

    private async Task<(Guid repoId, Guid llmId)> SeedAsync()
    {
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "repo",
            CloneUrl = "https://example.com/r.git"
        };
        var llm = new LlmSetting
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "default",
            Provider = LlmProvider.OpenAI,
            ApiKey = "sk-test",
            Model = "gpt-4o"
        };
        ctx.Repositories.Add(repo);
        ctx.LlmSettings.Add(llm);
        await ctx.SaveChangesAsync();
        return (repo.Id, llm.Id);
    }

    [Fact]
    public async Task SetLlm_OwnerPinsSetting()
    {
        var (repoId, llmId) = await SeedAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var result = await service.SetLlmSettingAsync(repoId, llmId, "alice");
        Assert.Equal(SetLlmResult.Updated, result);

        var repo = await ctx.Repositories.FindAsync(repoId);
        Assert.Equal(llmId, repo!.LlmSettingId);
    }

    [Fact]
    public async Task SetLlm_ClearsSettingWhenNull()
    {
        var (repoId, llmId) = await SeedAsync();
        await using (var ctx = NewContext())
        {
            await NewService(ctx).SetLlmSettingAsync(repoId, llmId, "alice");
        }
        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).SetLlmSettingAsync(repoId, null, "alice");
            Assert.Equal(SetLlmResult.Updated, result);
            var repo = await ctx.Repositories.FindAsync(repoId);
            Assert.Null(repo!.LlmSettingId);
        }
    }

    [Fact]
    public async Task SetLlm_NonOwnerRejected()
    {
        var (repoId, llmId) = await SeedAsync();
        await using var ctx = NewContext();
        var result = await NewService(ctx).SetLlmSettingAsync(repoId, llmId, "mallory");
        Assert.Equal(SetLlmResult.NotOwner, result);
    }

    [Fact]
    public async Task SetLlm_UnknownRepositoryReturnsNotFound()
    {
        await using var ctx = NewContext();
        var result = await NewService(ctx).SetLlmSettingAsync(Guid.NewGuid(), Guid.NewGuid(), "alice");
        Assert.Equal(SetLlmResult.RepositoryNotFound, result);
    }

    [Fact]
    public async Task SetLlm_UnknownLlmReturnsNotFound()
    {
        var (repoId, _) = await SeedAsync();
        await using var ctx = NewContext();
        var result = await NewService(ctx).SetLlmSettingAsync(repoId, Guid.NewGuid(), "alice");
        Assert.Equal(SetLlmResult.LlmSettingNotFound, result);
    }

    [Fact]
    public async Task SetLlm_OtherUsersLlmIsNotValid()
    {
        var (repoId, _) = await SeedAsync();
        await using (var ctx = NewContext())
        {
            ctx.LlmSettings.Add(new LlmSetting
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "bob",
                Name = "bob-llm",
                Provider = LlmProvider.Anthropic,
                ApiKey = "sk-bob",
                Model = "claude-opus-4-6"
            });
            await ctx.SaveChangesAsync();
        }

        var bobLlmId = Guid.Empty;
        await using (var ctx = NewContext())
        {
            bobLlmId = await ctx.LlmSettings.Where(l => l.OwnerUserId == "bob").Select(l => l.Id).FirstAsync();
        }

        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).SetLlmSettingAsync(repoId, bobLlmId, "alice");
            Assert.Equal(SetLlmResult.LlmSettingNotFound, result);
        }
    }
}
