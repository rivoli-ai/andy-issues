// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

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

public class SandboxServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly FakeContainersClient _containers = new();
    private readonly IConfiguration _config;

    public SandboxServiceTests()
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
        new(ctx, _containers, new RepositoryAccessGuard(ctx), _config,
            NullLogger<SandboxService>.Instance);

    private async Task<Guid> SeedRepoAsync(string owner = "alice", bool shareWith = false)
    {
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner,
            Name = "my-repo",
            CloneUrl = "https://example.com/r.git"
        };
        if (shareWith)
            repo.AddShare("bob", owner);
        ctx.Repositories.Add(repo);
        await ctx.SaveChangesAsync();
        return repo.Id;
    }

    [Fact]
    public async Task Create_Owner_PersistsSandboxAndCallsClient()
    {
        var repoId = await SeedRepoAsync();
        await using var ctx = NewContext();
        var dto = await NewService(ctx).CreateAsync(
            new CreateSandboxRequest(repoId, "feature/x", null),
            "alice");

        Assert.NotNull(dto);
        Assert.Equal(repoId, dto!.RepositoryId);
        Assert.Equal("feature/x", dto.Branch);
        Assert.Single(_containers.CreateCalls);
        Assert.Equal("ubuntu-dev-test", _containers.CreateCalls[0].templateCode);
        Assert.StartsWith("andy-sbx-my-repo-feature-x-", _containers.CreateCalls[0].name);
        Assert.Equal(SandboxStatus.Creating.ToString(), dto.Status);

        await using var ctx2 = NewContext();
        Assert.Equal(1, await ctx2.Sandboxes.CountAsync());
    }

    [Fact]
    public async Task Create_Stranger_Rejected()
    {
        var repoId = await SeedRepoAsync();
        await using var ctx = NewContext();
        var dto = await NewService(ctx).CreateAsync(
            new CreateSandboxRequest(repoId, "main", null),
            "mallory");
        Assert.Null(dto);
        Assert.Empty(_containers.CreateCalls);
    }

    [Fact]
    public async Task Create_SharedUser_Succeeds()
    {
        var repoId = await SeedRepoAsync(shareWith: true);
        await using var ctx = NewContext();
        var dto = await NewService(ctx).CreateAsync(
            new CreateSandboxRequest(repoId, "main", null),
            "bob");
        Assert.NotNull(dto);
        Assert.Equal("bob", dto!.OwnerUserId);
    }

    [Fact]
    public async Task List_RefreshesStatusFromClient()
    {
        var repoId = await SeedRepoAsync();
        Guid sandboxId;
        await using (var ctx = NewContext())
        {
            var dto = await NewService(ctx).CreateAsync(
                new CreateSandboxRequest(repoId, "main", null), "alice");
            sandboxId = dto!.Id;
        }

        // Flip remote status to Running.
        var containerId = _containers.Containers.Keys.Single();
        _containers.SeedContainer(containerId, "n", "running", ide: "https://ide/1", vnc: null);

        await using (var ctx = NewContext())
        {
            var list = await NewService(ctx).ListAsync("alice");
            var dto = Assert.Single(list);
            Assert.Equal(sandboxId, dto.Id);
            Assert.Equal(SandboxStatus.Running.ToString(), dto.Status);
            Assert.Equal("https://ide/1", dto.IdeEndpoint);
        }
    }

    [Fact]
    public async Task Get_NotOwner_ReturnsNull()
    {
        var repoId = await SeedRepoAsync(shareWith: true);
        Guid sandboxId;
        await using (var ctx = NewContext())
        {
            var dto = await NewService(ctx).CreateAsync(
                new CreateSandboxRequest(repoId, "main", null), "bob");
            sandboxId = dto!.Id;
        }

        await using (var ctx = NewContext())
        {
            var got = await NewService(ctx).GetAsync(sandboxId, "alice");
            Assert.Null(got);
        }
    }

    [Fact]
    public async Task Get_RemoteMissing_MarksDestroyed()
    {
        var repoId = await SeedRepoAsync();
        Guid sandboxId;
        string containerId;
        await using (var ctx = NewContext())
        {
            var dto = await NewService(ctx).CreateAsync(
                new CreateSandboxRequest(repoId, "main", null), "alice");
            sandboxId = dto!.Id;
            containerId = dto.ContainerId;
        }

        _containers.RemoveContainer(containerId);

        await using (var ctx = NewContext())
        {
            var got = await NewService(ctx).GetAsync(sandboxId, "alice");
            Assert.NotNull(got);
            Assert.Equal(SandboxStatus.Destroyed.ToString(), got!.Status);
        }
    }

    [Fact]
    public async Task Destroy_Owner_RemovesLocalAndCallsClient()
    {
        var repoId = await SeedRepoAsync();
        Guid sandboxId;
        string containerId;
        await using (var ctx = NewContext())
        {
            var dto = await NewService(ctx).CreateAsync(
                new CreateSandboxRequest(repoId, "main", null), "alice");
            sandboxId = dto!.Id;
            containerId = dto.ContainerId;
        }

        await using (var ctx = NewContext())
        {
            var ok = await NewService(ctx).DestroyAsync(sandboxId, "alice");
            Assert.True(ok);
        }

        Assert.Contains(containerId, _containers.DestroyCalls);
        await using (var ctx = NewContext())
        {
            Assert.Null(await ctx.Sandboxes.FindAsync(sandboxId));
        }
    }

    [Fact]
    public async Task Destroy_NotOwner_Rejected()
    {
        var repoId = await SeedRepoAsync(shareWith: true);
        Guid sandboxId;
        await using (var ctx = NewContext())
        {
            var dto = await NewService(ctx).CreateAsync(
                new CreateSandboxRequest(repoId, "main", null), "bob");
            sandboxId = dto!.Id;
        }

        await using (var ctx = NewContext())
        {
            var ok = await NewService(ctx).DestroyAsync(sandboxId, "alice");
            Assert.False(ok);
        }

        Assert.Empty(_containers.DestroyCalls);
    }

    [Fact]
    public async Task GetConnectionInfo_Owner_ReturnsRemote()
    {
        var repoId = await SeedRepoAsync();
        Guid sandboxId;
        string containerId;
        await using (var ctx = NewContext())
        {
            var dto = await NewService(ctx).CreateAsync(
                new CreateSandboxRequest(repoId, "main", null), "alice");
            sandboxId = dto!.Id;
            containerId = dto.ContainerId;
        }

        _containers.SeedContainer(containerId, "n", "Running", ide: "https://ide/2", vnc: "vnc://1");

        await using (var ctx = NewContext())
        {
            var info = await NewService(ctx).GetConnectionInfoAsync(sandboxId, "alice");
            Assert.NotNull(info);
            Assert.Equal("https://ide/2", info!.IdeEndpoint);
            Assert.Equal("vnc://1", info.VncEndpoint);
            Assert.Equal("ssh://10.0.0.1:22", info.SshEndpoint);
        }
    }

    [Fact]
    public async Task Create_UnknownRepo_ReturnsNull()
    {
        await using var ctx = NewContext();
        var dto = await NewService(ctx).CreateAsync(
            new CreateSandboxRequest(Guid.NewGuid(), "main", null), "alice");
        Assert.Null(dto);
    }
}
