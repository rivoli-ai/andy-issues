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

    private RepositoryService NewService(AppDbContext ctx, ICodeIndexClient? codeIndex = null)
    {
        var guard = new RepositoryAccessGuard(ctx);
        var dir = new UserDirectoryService(ctx);
        var gh = new StubGitHubClient();
        var az = new StubAzureDevOpsClient();
        var ci = codeIndex ?? new StubCodeIndexClient();
        var ss = new StubSecretStore();
        return new RepositoryService(ctx, guard, dir, gh, az, ci, ss,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RepositoryService>.Instance);
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

    // MARK: - CreateAsync

    [Fact]
    public async Task Create_PersistsRepositoryAndReturnsDto()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, dto) = await service.CreateAsync(
            new CreateRepositoryRequest(
                Name: "rivoli-ai/andy-issues",
                Description: "Issues service",
                Provider: "github",
                CloneUrl: "https://github.com/rivoli-ai/andy-issues.git",
                DefaultBranch: "main",
                ExternalId: null),
            ownerUserId: "alice");

        Assert.Equal(CreateRepositoryResult.Created, result);
        Assert.NotNull(dto);
        Assert.Equal("rivoli-ai/andy-issues", dto!.Name);
        Assert.Equal("github", dto.Provider, ignoreCase: true);
        Assert.Equal("main", dto.DefaultBranch);

        await using var verify = NewContext();
        var persisted = await verify.Repositories.SingleAsync();
        Assert.Equal("alice", persisted.OwnerUserId);
        Assert.Equal(RepositoryProvider.GitHub, persisted.Provider);
        Assert.Equal("https://github.com/rivoli-ai/andy-issues.git", persisted.CloneUrl);
    }

    [Fact]
    public async Task Create_DefaultsBranchToMainWhenOmitted()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, dto) = await service.CreateAsync(
            new CreateRepositoryRequest(
                Name: "x",
                Description: null,
                Provider: "github",
                CloneUrl: "https://github.com/rivoli-ai/x.git",
                DefaultBranch: null,
                ExternalId: null),
            ownerUserId: "alice");

        Assert.Equal(CreateRepositoryResult.Created, result);
        Assert.Equal("main", dto!.DefaultBranch);
    }

    [Fact]
    public async Task Create_AcceptsAzureDevOpsProviderCaseInsensitive()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, _) = await service.CreateAsync(
            new CreateRepositoryRequest(
                Name: "x",
                Description: null,
                Provider: "AzureDevOps",
                CloneUrl: "https://dev.azure.com/org/project/_git/x",
                DefaultBranch: "main",
                ExternalId: null),
            ownerUserId: "alice");

        Assert.Equal(CreateRepositoryResult.Created, result);
    }

    [Fact]
    public async Task Create_RejectsUnknownProvider()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, dto) = await service.CreateAsync(
            new CreateRepositoryRequest(
                Name: "x",
                Description: null,
                Provider: "gitlab",
                CloneUrl: "https://gitlab.com/x.git",
                DefaultBranch: "main",
                ExternalId: null),
            ownerUserId: "alice");

        Assert.Equal(CreateRepositoryResult.InvalidProvider, result);
        Assert.Null(dto);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/x.git")]
    [InlineData("git@github.com:rivoli-ai/x.git")] // SSH form is rejected — http(s) only
    public async Task Create_RejectsInvalidCloneUrl(string cloneUrl)
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, dto) = await service.CreateAsync(
            new CreateRepositoryRequest(
                Name: "x",
                Description: null,
                Provider: "github",
                CloneUrl: cloneUrl,
                DefaultBranch: "main",
                ExternalId: null),
            ownerUserId: "alice");

        Assert.Equal(CreateRepositoryResult.InvalidCloneUrl, result);
        Assert.Null(dto);
    }

    [Fact]
    public async Task Create_DuplicateForSameOwnerReturnsAlreadyExistsWithExistingDto()
    {
        // Conductor's "Add repository" sheet treats AlreadyExists as a
        // benign no-op so resubmitting the same clone URL must surface
        // the existing row, not a 500.
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var first = await service.CreateAsync(
            new CreateRepositoryRequest(
                Name: "x",
                Description: null,
                Provider: "github",
                CloneUrl: "https://github.com/rivoli-ai/x.git",
                DefaultBranch: "main",
                ExternalId: null),
            ownerUserId: "alice");
        Assert.Equal(CreateRepositoryResult.Created, first.Result);

        var second = await service.CreateAsync(
            new CreateRepositoryRequest(
                Name: "x",
                Description: null,
                Provider: "github",
                CloneUrl: "https://github.com/rivoli-ai/x.git",
                DefaultBranch: "main",
                ExternalId: null),
            ownerUserId: "alice");

        Assert.Equal(CreateRepositoryResult.AlreadyExists, second.Result);
        Assert.NotNull(second.Dto);
        Assert.Equal(first.Dto!.Id, second.Dto!.Id);
    }

    [Fact]
    public async Task Create_SameCloneUrlForDifferentOwnersBothSucceed()
    {
        // Two users may legitimately track the same upstream repo.
        // Idempotency is per-owner, not global.
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var alice = await service.CreateAsync(
            new CreateRepositoryRequest(
                Name: "x", Description: null, Provider: "github",
                CloneUrl: "https://github.com/rivoli-ai/x.git",
                DefaultBranch: "main", ExternalId: null),
            ownerUserId: "alice");
        var bob = await service.CreateAsync(
            new CreateRepositoryRequest(
                Name: "x", Description: null, Provider: "github",
                CloneUrl: "https://github.com/rivoli-ai/x.git",
                DefaultBranch: "main", ExternalId: null),
            ownerUserId: "bob");

        Assert.Equal(CreateRepositoryResult.Created, alice.Result);
        Assert.Equal(CreateRepositoryResult.Created, bob.Result);
        Assert.NotEqual(alice.Dto!.Id, bob.Dto!.Id);
    }

    // MARK: - Code index auto-registration

    [Fact]
    public async Task Create_RegistersWithCodeIndexAndSetsStatusToIndexing()
    {
        var stub = new StubCodeIndexClient();
        await using var ctx = NewContext();
        var service = NewService(ctx, stub);

        var (result, dto) = await service.CreateAsync(
            new CreateRepositoryRequest(
                Name: "x", Description: null, Provider: "github",
                CloneUrl: "https://github.com/rivoli-ai/x.git",
                DefaultBranch: "main", ExternalId: null),
            ownerUserId: "alice");

        Assert.Equal(CreateRepositoryResult.Created, result);
        Assert.Single(stub.RegisterCalls);
        Assert.Equal("https://github.com/rivoli-ai/x.git", stub.RegisterCalls[0].cloneUrl);
        Assert.Equal("main", stub.RegisterCalls[0].defaultBranch);

        // Status should now be Indexing
        Assert.Equal("Indexing", dto!.CodeIndexStatus);
    }

    [Fact]
    public async Task Create_CodeIndexFailureDoesNotFailRepoCreation()
    {
        var stub = new StubCodeIndexClient
        {
            RegistrationResult = new CodeIndexRegistrationResult(
                CodeIndexRegistrationOutcome.ServiceUnavailable, null, "down")
        };
        await using var ctx = NewContext();
        var service = NewService(ctx, stub);

        var (result, dto) = await service.CreateAsync(
            new CreateRepositoryRequest(
                Name: "x", Description: null, Provider: "github",
                CloneUrl: "https://github.com/rivoli-ai/x.git",
                DefaultBranch: "main", ExternalId: null),
            ownerUserId: "alice");

        Assert.Equal(CreateRepositoryResult.Created, result);
        Assert.NotNull(dto);
        // Status stays NotIndexed when registration fails
        Assert.Equal("NotIndexed", dto!.CodeIndexStatus);
    }

    // MARK: - Existing list/share tests

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

    // ── #100 — provider filter ──────────────────────────────────────

    private async Task SeedMixedProvidersAsync()
    {
        await using var ctx = NewContext();
        ctx.Repositories.AddRange(
            new Repository
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "alice",
                Name = "alice-gh",
                CloneUrl = "https://github.com/alice/gh.git",
                Provider = Andy.Issues.Domain.Enums.RepositoryProvider.GitHub
            },
            new Repository
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "alice",
                Name = "alice-azdo",
                CloneUrl = "https://dev.azure.com/alice/_git/azdo",
                Provider = Andy.Issues.Domain.Enums.RepositoryProvider.AzureDevOps
            });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task List_ProviderFilter_GitHub_OnlyReturnsGitHubRepos()
    {
        await SeedMixedProvidersAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var result = await service.ListAsync("alice", RepositoryScope.Mine, 1, 20,
            provider: Andy.Issues.Domain.Enums.RepositoryProvider.GitHub);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("alice-gh", result.Items[0].Name);
    }

    [Fact]
    public async Task List_ProviderFilter_AzureDevOps_OnlyReturnsAzureRepos()
    {
        await SeedMixedProvidersAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var result = await service.ListAsync("alice", RepositoryScope.Mine, 1, 20,
            provider: Andy.Issues.Domain.Enums.RepositoryProvider.AzureDevOps);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("alice-azdo", result.Items[0].Name);
    }

    [Fact]
    public async Task List_ProviderFilter_Null_ReturnsAllProviders()
    {
        await SeedMixedProvidersAsync();
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var result = await service.ListAsync("alice", RepositoryScope.Mine, 1, 20, provider: null);

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task List_ProviderFilter_ComposesWithScope()
    {
        // Seed: alice owns GH; bob owns AzDO and shares it with alice.
        await using (var ctx = NewContext())
        {
            ctx.Repositories.AddRange(
                new Repository
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = "alice",
                    Name = "alice-gh",
                    CloneUrl = "https://github.com/alice/gh.git",
                    Provider = Andy.Issues.Domain.Enums.RepositoryProvider.GitHub
                });
            var bobAzdo = new Repository
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "bob",
                Name = "bob-azdo",
                CloneUrl = "https://dev.azure.com/bob/_git/azdo",
                Provider = Andy.Issues.Domain.Enums.RepositoryProvider.AzureDevOps
            };
            bobAzdo.AddShare("alice", "bob");
            ctx.Repositories.Add(bobAzdo);
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        var service = NewService(ctx2);

        // All scope + AzDO filter → only the shared bob-azdo.
        var azdo = await service.ListAsync("alice", RepositoryScope.All, 1, 20,
            provider: Andy.Issues.Domain.Enums.RepositoryProvider.AzureDevOps);
        Assert.Single(azdo.Items);
        Assert.Equal("bob-azdo", azdo.Items[0].Name);
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
