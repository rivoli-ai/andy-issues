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
        new(ctx, _containers, new RepositoryAccessGuard(ctx),
            new ArtifactFeedService(ctx), new McpConfigService(ctx),
            _config, NullLogger<SandboxService>.Instance);

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

    [Fact]
    public async Task Create_WithAzureIdentity_PropagatesAzureEnvVars()
    {
        var repoId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.Repositories.Add(new Repository
            {
                Id = repoId,
                OwnerUserId = "alice",
                Name = "az-repo",
                CloneUrl = "https://example.com/a.git",
                AzureClientId = "client-guid",
                AzureClientSecret = "supersecret",
                AzureTenantId = "tenant-guid",
                AzureSubscriptionId = "sub-guid"
            });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        var dto = await NewService(ctx2).CreateAsync(
            new CreateSandboxRequest(repoId, "main", null), "alice");
        Assert.NotNull(dto);

        var call = Assert.Single(_containers.CreateCalls);
        Assert.NotNull(call.environmentVariables);
        Assert.Equal("client-guid", call.environmentVariables!["AZURE_CLIENT_ID"]);
        Assert.Equal("supersecret", call.environmentVariables["AZURE_CLIENT_SECRET"]);
        Assert.Equal("tenant-guid", call.environmentVariables["AZURE_TENANT_ID"]);
        Assert.Equal("sub-guid", call.environmentVariables["AZURE_SUBSCRIPTION_ID"]);
    }

    [Fact]
    public async Task Create_WithoutAzureIdentity_OmitsEnvVars()
    {
        var repoId = await SeedRepoAsync();
        await using var ctx = NewContext();
        var dto = await NewService(ctx).CreateAsync(
            new CreateSandboxRequest(repoId, "main", null), "alice");
        Assert.NotNull(dto);

        var call = Assert.Single(_containers.CreateCalls);
        Assert.Null(call.environmentVariables);
    }

    [Fact]
    public async Task Create_AzureIdentityWithoutSubscription_SkipsSubscriptionVar()
    {
        var repoId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.Repositories.Add(new Repository
            {
                Id = repoId,
                OwnerUserId = "alice",
                Name = "az-repo",
                CloneUrl = "https://example.com/a.git",
                AzureClientId = "c",
                AzureClientSecret = "s",
                AzureTenantId = "t"
                // AzureSubscriptionId intentionally omitted
            });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        await NewService(ctx2).CreateAsync(new CreateSandboxRequest(repoId, "main", null), "alice");

        var env = _containers.CreateCalls[0].environmentVariables!;
        Assert.False(env.ContainsKey("AZURE_SUBSCRIPTION_ID"));
        Assert.Equal("c", env["AZURE_CLIENT_ID"]);
    }

    [Fact]
    public async Task Create_WithEnabledArtifactFeeds_SerializesToJsonAndAttachesPat()
    {
        var repoId = await SeedRepoAsync();
        await using (var ctx = NewContext())
        {
            ctx.ArtifactFeedConfigs.AddRange(
                new ArtifactFeedConfig
                {
                    Id = Guid.NewGuid(),
                    Name = "internal-nuget",
                    Organization = "rivoli",
                    FeedName = "pkgs",
                    Project = "shared",
                    Type = ArtifactFeedType.Nuget,
                    Enabled = true
                },
                new ArtifactFeedConfig
                {
                    Id = Guid.NewGuid(),
                    Name = "legacy",
                    Organization = "rivoli",
                    FeedName = "legacy-pkgs",
                    Type = ArtifactFeedType.Pip,
                    Enabled = false // disabled — should be skipped
                });
            ctx.LinkedProviders.Add(new LinkedProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "alice",
                Provider = LinkedProviderKind.AzureDevOps,
                AccessToken = "pat-from-feeds"
            });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        await NewService(ctx2).CreateAsync(new CreateSandboxRequest(repoId, "main", null), "alice");

        var env = _containers.CreateCalls[0].environmentVariables!;
        Assert.True(env.ContainsKey("ARTIFACT_FEEDS_JSON"));
        Assert.Equal("pat-from-feeds", env["AZURE_DEVOPS_PAT"]);

        using var doc = System.Text.Json.JsonDocument.Parse(env["ARTIFACT_FEEDS_JSON"]);
        var items = doc.RootElement.EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("internal-nuget", items[0].GetProperty("name").GetString());
        Assert.Equal("Nuget", items[0].GetProperty("type").GetString());
        Assert.Equal("rivoli", items[0].GetProperty("organization").GetString());
        Assert.Equal("pkgs", items[0].GetProperty("feedName").GetString());
        Assert.Equal("shared", items[0].GetProperty("project").GetString());
    }

    [Fact]
    public async Task Create_NoArtifactFeeds_OmitsFeedEnvVars()
    {
        var repoId = await SeedRepoAsync();
        await using var ctx = NewContext();
        await NewService(ctx).CreateAsync(new CreateSandboxRequest(repoId, "main", null), "alice");

        Assert.Null(_containers.CreateCalls[0].environmentVariables);
    }

    [Fact]
    public async Task Create_ArtifactFeedsWithoutPat_OmitsPatVar()
    {
        var repoId = await SeedRepoAsync();
        await using (var ctx = NewContext())
        {
            ctx.ArtifactFeedConfigs.Add(new ArtifactFeedConfig
            {
                Id = Guid.NewGuid(),
                Name = "feed",
                Organization = "rivoli",
                FeedName = "p",
                Type = ArtifactFeedType.Npm,
                Enabled = true
            });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        await NewService(ctx2).CreateAsync(new CreateSandboxRequest(repoId, "main", null), "alice");

        var env = _containers.CreateCalls[0].environmentVariables!;
        Assert.True(env.ContainsKey("ARTIFACT_FEEDS_JSON"));
        Assert.False(env.ContainsKey("AZURE_DEVOPS_PAT"));
    }

    [Fact]
    public async Task Create_WithEnabledMcpConfigs_SerializesFullConfigsWithSecrets()
    {
        var repoId = await SeedRepoAsync();
        await using (var ctx = NewContext())
        {
            ctx.McpServerConfigs.AddRange(
                new McpServerConfig
                {
                    Id = Guid.NewGuid(),
                    Name = "personal-stdio",
                    OwnerUserId = "alice",
                    IsShared = false,
                    Type = McpServerType.Stdio,
                    Enabled = true,
                    Command = "python",
                    ArgumentsJson = "[\"-m\",\"my_mcp\"]",
                    EnvironmentJson = "{\"API_KEY\":\"secret-alice\"}"
                },
                new McpServerConfig
                {
                    Id = Guid.NewGuid(),
                    Name = "shared-remote",
                    OwnerUserId = null,
                    IsShared = true,
                    Type = McpServerType.Remote,
                    Enabled = true,
                    Url = "https://mcp.example.com",
                    HeadersJson = "{\"Authorization\":\"Bearer shared-token\"}"
                },
                new McpServerConfig
                {
                    Id = Guid.NewGuid(),
                    Name = "bob-personal",
                    OwnerUserId = "bob",
                    IsShared = false,
                    Type = McpServerType.Stdio,
                    Enabled = true,
                    Command = "node"
                },
                new McpServerConfig
                {
                    Id = Guid.NewGuid(),
                    Name = "disabled-shared",
                    OwnerUserId = null,
                    IsShared = true,
                    Type = McpServerType.Remote,
                    Enabled = false,
                    Url = "https://off.example.com"
                });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        await NewService(ctx2).CreateAsync(new CreateSandboxRequest(repoId, "main", null), "alice");

        var env = _containers.CreateCalls[0].environmentVariables!;
        Assert.True(env.ContainsKey("MCP_SERVERS_JSON"));

        using var doc = System.Text.Json.JsonDocument.Parse(env["MCP_SERVERS_JSON"]);
        var items = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        // Ordered by name: personal-stdio, shared-remote (bob-personal excluded, disabled-shared excluded).
        Assert.Equal("personal-stdio", items[0].GetProperty("name").GetString());
        Assert.Equal("{\"API_KEY\":\"secret-alice\"}",
            items[0].GetProperty("environmentJson").GetString());

        Assert.Equal("shared-remote", items[1].GetProperty("name").GetString());
        Assert.Equal("{\"Authorization\":\"Bearer shared-token\"}",
            items[1].GetProperty("headersJson").GetString());
    }

    [Fact]
    public async Task McpConfigDto_StillMasksSecretsOnOutboundPath()
    {
        // Sanity check that mapping to the public DTO never surfaces the JSON columns.
        var entity = new McpServerConfig
        {
            Id = Guid.NewGuid(),
            Name = "n",
            OwnerUserId = "alice",
            Type = McpServerType.Stdio,
            EnvironmentJson = "{\"SECRET\":\"v\"}",
            HeadersJson = "{\"X\":\"Y\"}"
        };
        var dto = Andy.Issues.Application.Mapping.McpServerConfigMapping.ToDto(entity);
        Assert.True(dto.HasEnvironment);
        Assert.True(dto.HasHeaders);
        Assert.DoesNotContain("SECRET", System.Text.Json.JsonSerializer.Serialize(dto));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Create_NoMcpConfigs_OmitsMcpEnvVar()
    {
        var repoId = await SeedRepoAsync();
        await using var ctx = NewContext();
        await NewService(ctx).CreateAsync(new CreateSandboxRequest(repoId, "main", null), "alice");
        Assert.Null(_containers.CreateCalls[0].environmentVariables);
    }

    [Fact]
    public void BuildEnvironmentVariables_NoAzureIdentity_ReturnsNull()
    {
        var repo = new Repository { Id = Guid.NewGuid(), Name = "r", CloneUrl = "x" };
        Assert.Null(SandboxService.BuildEnvironmentVariables(repo));
    }

    [Fact]
    public void BuildEnvironmentVariables_PartialAzureIdentity_IsTreatedAsAbsent()
    {
        // HasAzureIdentity requires all three of ClientId/Secret/TenantId.
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "r",
            CloneUrl = "x",
            AzureClientId = "c",
            AzureClientSecret = "s"
            // Tenant missing
        };
        Assert.Null(SandboxService.BuildEnvironmentVariables(repo));
    }
}
