// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Integration.Data;

public class AppDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AppDbContextTests()
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

    [Fact]
    public async Task Repository_RoundTrip_PersistsFields()
    {
        var id = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.Repositories.Add(new Repository
            {
                Id = id,
                OwnerUserId = "owner",
                Name = "demo",
                Provider = RepositoryProvider.GitHub,
                CloneUrl = "https://example.com/demo.git",
                DefaultBranch = "main"
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.Repositories.FindAsync(id);
            Assert.NotNull(loaded);
            Assert.Equal("demo", loaded!.Name);
            Assert.Equal(RepositoryProvider.GitHub, loaded.Provider);
        }
    }

    [Fact]
    public async Task BacklogHierarchy_CascadeDeletesOnRepositoryRemoval()
    {
        var repoId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            var repo = new Repository
            {
                Id = repoId,
                OwnerUserId = "owner",
                Name = "cascade",
                CloneUrl = "https://example.com/c.git"
            };
            var epic = new Epic { Id = Guid.NewGuid(), RepositoryId = repoId, Title = "E1" };
            var feature = new Feature { Id = Guid.NewGuid(), EpicId = epic.Id, Title = "F1" };
            var story = new UserStory { Id = Guid.NewGuid(), FeatureId = feature.Id, Title = "S1" };

            repo.Epics.Add(epic);
            epic.Features.Add(feature);
            feature.Stories.Add(story);

            ctx.Repositories.Add(repo);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            var repo = await ctx.Repositories.FindAsync(repoId);
            ctx.Repositories.Remove(repo!);
            await ctx.SaveChangesAsync();

            Assert.Empty(await ctx.Epics.ToListAsync());
            Assert.Empty(await ctx.Features.ToListAsync());
            Assert.Empty(await ctx.UserStories.ToListAsync());
        }
    }

    [Fact]
    public async Task RepositoryShare_UniqueIndexPreventsDuplicate()
    {
        var repoId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.Repositories.Add(new Repository
            {
                Id = repoId,
                OwnerUserId = "owner",
                Name = "share",
                CloneUrl = "https://example.com/s.git"
            });
            ctx.RepositoryShares.Add(new RepositoryShare
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                SharedWithUserId = "alice",
                GrantedByUserId = "owner"
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            ctx.RepositoryShares.Add(new RepositoryShare
            {
                Id = Guid.NewGuid(),
                RepositoryId = repoId,
                SharedWithUserId = "alice",
                GrantedByUserId = "owner"
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task McpServerConfig_UniqueNamePerOwner()
    {
        await using (var ctx = NewContext())
        {
            ctx.McpServerConfigs.Add(new McpServerConfig
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "bob",
                Name = "dup",
                Type = McpServerType.Stdio
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            ctx.McpServerConfigs.Add(new McpServerConfig
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "bob",
                Name = "dup",
                Type = McpServerType.Stdio
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Sandbox_RoundTrip_AndUniqueContainerId()
    {
        var repoId = Guid.NewGuid();
        var containerId = $"ctr-{Guid.NewGuid():N}";
        await using (var ctx = NewContext())
        {
            ctx.Repositories.Add(new Repository
            {
                Id = repoId,
                OwnerUserId = "owner",
                Name = "sb",
                CloneUrl = "https://example.com/sb.git"
            });
            ctx.Sandboxes.Add(new Sandbox
            {
                Id = Guid.NewGuid(),
                ContainerId = containerId,
                RepositoryId = repoId,
                OwnerUserId = "owner",
                Branch = "main",
                Status = SandboxStatus.Pending
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            ctx.Sandboxes.Add(new Sandbox
            {
                Id = Guid.NewGuid(),
                ContainerId = containerId,
                RepositoryId = repoId,
                OwnerUserId = "owner",
                Branch = "feature",
                Status = SandboxStatus.Pending
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task LinkedProvider_UniquePerOwnerAndProvider()
    {
        await using (var ctx = NewContext())
        {
            ctx.LinkedProviders.Add(new LinkedProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "u",
                Provider = LinkedProviderKind.GitHub,
                AccessToken = "t1"
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            ctx.LinkedProviders.Add(new LinkedProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "u",
                Provider = LinkedProviderKind.GitHub,
                AccessToken = "t2"
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task SimpleEntities_RoundTrip()
    {
        var artifactId = Guid.NewGuid();
        var llmId = Guid.NewGuid();

        await using (var ctx = NewContext())
        {
            ctx.ArtifactFeedConfigs.Add(new ArtifactFeedConfig
            {
                Id = artifactId,
                Name = "feed1",
                Organization = "org",
                FeedName = "pkg",
                Type = ArtifactFeedType.Nuget
            });
            ctx.LlmSettings.Add(new LlmSetting
            {
                Id = llmId,
                OwnerUserId = "u",
                Name = "default",
                Provider = LlmProvider.OpenAI,
                ApiKey = "k",
                Model = "gpt-4o"
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            Assert.NotNull(await ctx.ArtifactFeedConfigs.FindAsync(artifactId));
            var llm = await ctx.LlmSettings.FindAsync(llmId);
            Assert.NotNull(llm);
            Assert.Equal(LlmProvider.OpenAI, llm!.Provider);
        }
    }
}
