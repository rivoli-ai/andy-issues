// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

// Verifies that SandboxService writes the correct
// andy.issues.events.sandbox.* outbox row for each lifecycle event
// (Story 15.5).
public class SandboxServiceEventsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly FakeContainersClient _containers = new();
    private readonly IConfiguration _config;

    public SandboxServiceEventsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new AppDbContext(_options);
        ctx.Database.EnsureCreated();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AndyContainers:DefaultTemplateCode"] = "ubuntu-dev-test"
            })
            .Build();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext NewContext() => new(_options);
    private SandboxService NewService(AppDbContext ctx) =>
        new(ctx, _containers, new RepositoryAccessGuard(ctx),
            new ArtifactFeedService(ctx), new McpConfigService(ctx),
            _config, NullLogger<SandboxService>.Instance);

    private async Task<Guid> SeedRepoAsync()
    {
        await using var ctx = NewContext();
        ctx.Repositories.Add(new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "repo",
            CloneUrl = "https://example.com/r.git"
        });
        await ctx.SaveChangesAsync();
        return ctx.Repositories.Single().Id;
    }

    [Fact]
    public async Task Create_EmitsAttachedEvent()
    {
        var repoId = await SeedRepoAsync();
        await using (var ctx = NewContext())
        {
            var dto = await NewService(ctx).CreateAsync(
                new CreateSandboxRequest(repoId, "feature/x", null), "alice");
            Assert.NotNull(dto);
        }

        await using var verify = NewContext();
        var entry = await verify.Outbox.SingleAsync();
        Assert.EndsWith(".attached", entry.Subject);

        using var doc = JsonDocument.Parse(entry.PayloadJson);
        var root = doc.RootElement;
        Assert.Equal(repoId.ToString(), root.GetProperty("repository_id").GetString());
        Assert.Equal("feature/x", root.GetProperty("branch").GetString());
        Assert.Equal(SandboxEventPayload.SchemaVersion, root.GetProperty("schema_version").GetInt32());
        Assert.False(root.TryGetProperty("reason", out _),
            "reason is omitted when null");
    }

    [Fact]
    public async Task Destroy_EmitsDetachedEvent()
    {
        var repoId = await SeedRepoAsync();
        Guid sandboxId;
        await using (var ctx = NewContext())
        {
            sandboxId = (await NewService(ctx).CreateAsync(
                new CreateSandboxRequest(repoId, "main", null), "alice"))!.Id;
        }

        await using (var ctx = NewContext())
        {
            var ok = await NewService(ctx).DestroyAsync(sandboxId, "alice");
            Assert.True(ok);
        }

        await using var verify = NewContext();
        var entries = await verify.Outbox.OrderBy(e => e.CreatedAt).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.EndsWith(".attached", entries[0].Subject);
        Assert.EndsWith(".detached", entries[1].Subject);
    }

    [Fact]
    public async Task Refresh_RemoteGone_EmitsDetachedEvent()
    {
        var repoId = await SeedRepoAsync();
        Guid sandboxId;
        string containerId;
        await using (var ctx = NewContext())
        {
            var dto = await NewService(ctx).CreateAsync(
                new CreateSandboxRequest(repoId, "main", null), "alice");
            sandboxId = dto!.Id;
            containerId = _containers.Containers.Keys.First();
        }

        // Simulate remote container disappearing.
        _containers.RemoveContainer(containerId);

        await using (var ctx = NewContext())
        {
            await NewService(ctx).GetAsync(sandboxId, "alice");
        }

        await using var verify = NewContext();
        var lastEntry = await verify.Outbox.OrderByDescending(e => e.CreatedAt).FirstAsync();
        Assert.EndsWith(".detached", lastEntry.Subject);

        using var doc = JsonDocument.Parse(lastEntry.PayloadJson);
        Assert.Equal("Destroyed", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Refresh_RemoteFailed_EmitsFailedEventOnce()
    {
        var repoId = await SeedRepoAsync();
        Guid sandboxId;
        string containerId;
        await using (var ctx = NewContext())
        {
            sandboxId = (await NewService(ctx).CreateAsync(
                new CreateSandboxRequest(repoId, "main", null), "alice"))!.Id;
            containerId = _containers.Containers.Keys.First();
        }

        // Flip remote to Failed.
        _containers.SeedContainer(containerId, "name", "failed");

        // First refresh: sees Creating → Failed transition, emits failed.
        await using (var ctx = NewContext())
        {
            await NewService(ctx).GetAsync(sandboxId, "alice");
        }
        // Second refresh: status is already Failed, no new event.
        await using (var ctx = NewContext())
        {
            await NewService(ctx).GetAsync(sandboxId, "alice");
        }

        await using var verify = NewContext();
        var failedEntries = await verify.Outbox.Where(e => e.Subject.EndsWith(".failed")).ToListAsync();
        Assert.Single(failedEntries);

        using var doc = JsonDocument.Parse(failedEntries[0].PayloadJson);
        Assert.Equal("Failed", doc.RootElement.GetProperty("status").GetString());
        Assert.Contains("failed", doc.RootElement.GetProperty("reason").GetString()!);
    }
}
