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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class PullRequestServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly FakeContainersClient _containers = new();
    private readonly StubGitHubClient _github = new();
    private readonly StubAzureDevOpsClient _azure = new();

    public PullRequestServiceTests()
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
    private PullRequestService NewService(AppDbContext ctx) =>
        new(ctx, _containers, _github, _azure, NullLogger<PullRequestService>.Instance);

    private async Task<(Guid repoId, Guid sandboxId, Guid? storyId)> SeedGitHubAsync(
        string owner = "alice",
        Guid? storyId = null)
    {
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner,
            Name = "r",
            Provider = RepositoryProvider.GitHub,
            CloneUrl = "https://github.com/rivoli-ai/example.git",
            ExternalId = "1234"
        };
        ctx.Repositories.Add(repo);
        ctx.LinkedProviders.Add(new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner,
            Provider = LinkedProviderKind.GitHub,
            AccessToken = "gh-token"
        });

        var sandbox = new Sandbox
        {
            Id = Guid.NewGuid(),
            ContainerId = "ctr-fake",
            RepositoryId = repo.Id,
            OwnerUserId = owner,
            Branch = "feature/x"
        };
        ctx.Sandboxes.Add(sandbox);

        Guid? createdStoryId = null;
        if (storyId is not null)
        {
            var epic = new Epic { Id = Guid.NewGuid(), RepositoryId = repo.Id, Title = "E" };
            ctx.Epics.Add(epic);
            var feature = new Feature { Id = Guid.NewGuid(), EpicId = epic.Id, Title = "F" };
            ctx.Features.Add(feature);
            var story = new UserStory { Id = storyId.Value, FeatureId = feature.Id, Title = "S" };
            ctx.UserStories.Add(story);
            createdStoryId = story.Id;
        }

        await ctx.SaveChangesAsync();
        _containers.SeedContainer("ctr-fake", "n", "Running");
        return (repo.Id, sandbox.Id, createdStoryId);
    }

    private async Task<(Guid repoId, Guid sandboxId)> SeedAzureDevOpsAsync(string owner = "alice")
    {
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner,
            Name = "r",
            Provider = RepositoryProvider.AzureDevOps,
            CloneUrl = "https://dev.azure.com/myorg/myproject/_git/myrepo",
            ExternalId = "az-repo-guid"
        };
        ctx.Repositories.Add(repo);
        ctx.LinkedProviders.Add(new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner,
            Provider = LinkedProviderKind.AzureDevOps,
            AccessToken = "pat"
        });
        var sandbox = new Sandbox
        {
            Id = Guid.NewGuid(),
            ContainerId = "ctr-az",
            RepositoryId = repo.Id,
            OwnerUserId = owner,
            Branch = "feature/y"
        };
        ctx.Sandboxes.Add(sandbox);
        await ctx.SaveChangesAsync();
        _containers.SeedContainer("ctr-az", "n", "Running");
        return (repo.Id, sandbox.Id);
    }

    [Fact]
    public async Task Create_GitHubHappyPath_DispatchesToGitHubClient()
    {
        var (repoId, sandboxId, _) = await SeedGitHubAsync();
        _github.PullRequestResult = new GitHubPullRequestInfo(42, "https://github.com/rivoli-ai/example/pull/42");

        await using var ctx = NewContext();
        var result = await NewService(ctx).CreateFromSandboxAsync(
            repoId,
            new CreatePullRequestFromSandboxRequest(sandboxId, "hello", "world", "feature/x", "main", null),
            "alice");

        Assert.Equal(PullRequestOutcome.Created, result.Outcome);
        Assert.Equal("https://github.com/rivoli-ai/example/pull/42", result.PullRequestUrl);

        Assert.Single(_github.PullRequestCalls);
        var call = _github.PullRequestCalls[0];
        Assert.Equal("rivoli-ai", call.owner);
        Assert.Equal("example", call.repo);
        Assert.Equal("feature/x", call.head);
        Assert.Equal("main", call.baseBranch);
        Assert.Empty(_azure.PullRequestCalls);
    }

    [Fact]
    public async Task Create_AzureDevOpsHappyPath_DispatchesToAzureClient()
    {
        var (repoId, sandboxId) = await SeedAzureDevOpsAsync();
        _azure.PullRequestResult = new AzureDevOpsPullRequestInfo(7, "https://dev.azure.com/myorg/myproject/_git/az-repo-guid/pullrequest/7");

        await using var ctx = NewContext();
        var result = await NewService(ctx).CreateFromSandboxAsync(
            repoId,
            new CreatePullRequestFromSandboxRequest(sandboxId, "hi", null, "feature/y", "main", null),
            "alice");

        Assert.Equal(PullRequestOutcome.Created, result.Outcome);
        Assert.Single(_azure.PullRequestCalls);
        var call = _azure.PullRequestCalls[0];
        Assert.Equal("myorg", call.org);
        Assert.Equal("myproject", call.project);
        Assert.Equal("az-repo-guid", call.repoId);
        Assert.Empty(_github.PullRequestCalls);
    }

    [Fact]
    public async Task Create_UpdatesLinkedStoryPullRequestUrl()
    {
        var (repoId, sandboxId, storyId) = await SeedGitHubAsync(storyId: Guid.NewGuid());
        _github.PullRequestResult = new GitHubPullRequestInfo(1, "https://github.com/a/b/pull/1");

        await using (var ctx = NewContext())
        {
            var result = await NewService(ctx).CreateFromSandboxAsync(
                repoId,
                new CreatePullRequestFromSandboxRequest(sandboxId, "t", null, "feature/x", "main", storyId),
                "alice");
            Assert.Equal(PullRequestOutcome.Created, result.Outcome);
        }

        await using (var ctx = NewContext())
        {
            var story = await ctx.UserStories.FindAsync(storyId!.Value);
            Assert.Equal("https://github.com/a/b/pull/1", story!.PullRequestUrl);
        }
    }

    [Fact]
    public async Task Create_PushFails_ReturnsPushFailed()
    {
        var (repoId, sandboxId, _) = await SeedGitHubAsync();
        _github.PullRequestResult = new GitHubPullRequestInfo(1, "https://x/y");

        // Redirect exec to return a non-zero exit so the push is considered failed.
        _containers.SetExec((_, _) => new ContainerExecResult(1, "", "remote rejected"));

        await using var ctx = NewContext();
        var result = await NewService(ctx).CreateFromSandboxAsync(
            repoId,
            new CreatePullRequestFromSandboxRequest(sandboxId, "t", null, "feature/x", "main", null),
            "alice");

        Assert.Equal(PullRequestOutcome.PushFailed, result.Outcome);
        Assert.Contains("remote rejected", result.Error);
        Assert.Empty(_github.PullRequestCalls);
    }

    [Fact]
    public async Task Create_NotOwner_ReturnsForbidden()
    {
        var (repoId, sandboxId, _) = await SeedGitHubAsync();
        await using var ctx = NewContext();
        var result = await NewService(ctx).CreateFromSandboxAsync(
            repoId,
            new CreatePullRequestFromSandboxRequest(sandboxId, "t", null, "feature/x", "main", null),
            "mallory");
        Assert.Equal(PullRequestOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task Create_SandboxFromDifferentRepo_ReturnsNotFound()
    {
        var (repoId, _, _) = await SeedGitHubAsync();
        var (_, otherSandboxId) = await SeedAzureDevOpsAsync();

        await using var ctx = NewContext();
        var result = await NewService(ctx).CreateFromSandboxAsync(
            repoId,
            new CreatePullRequestFromSandboxRequest(otherSandboxId, "t", null, "feature/x", "main", null),
            "alice");
        Assert.Equal(PullRequestOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Create_MalformedGitHubCloneUrl_ReturnsProviderFailed()
    {
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "weird",
            Provider = RepositoryProvider.GitHub,
            CloneUrl = "https://example.com/not-github",
            ExternalId = "1"
        };
        ctx.Repositories.Add(repo);
        ctx.LinkedProviders.Add(new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Provider = LinkedProviderKind.GitHub,
            AccessToken = "gh"
        });
        var sandbox = new Sandbox
        {
            Id = Guid.NewGuid(),
            ContainerId = "ctr-weird",
            RepositoryId = repo.Id,
            OwnerUserId = "alice",
            Branch = "feature/x"
        };
        ctx.Sandboxes.Add(sandbox);
        await ctx.SaveChangesAsync();
        _containers.SeedContainer("ctr-weird", "n", "Running");

        var result = await NewService(ctx).CreateFromSandboxAsync(
            repo.Id,
            new CreatePullRequestFromSandboxRequest(sandbox.Id, "t", null, "feature/x", "main", null),
            "alice");
        Assert.Equal(PullRequestOutcome.ProviderFailed, result.Outcome);
        Assert.Empty(_github.PullRequestCalls);
    }

    [Fact]
    public void TryParseGitHubOwnerRepo_Cases()
    {
        Assert.True(PullRequestService.TryParseGitHubOwnerRepo(
            "https://github.com/rivoli-ai/example.git", out var owner, out var repo));
        Assert.Equal("rivoli-ai", owner);
        Assert.Equal("example", repo);

        Assert.True(PullRequestService.TryParseGitHubOwnerRepo(
            "https://github.com/rivoli-ai/example", out owner, out repo));
        Assert.Equal("example", repo);

        Assert.False(PullRequestService.TryParseGitHubOwnerRepo(
            "https://dev.azure.com/org/proj/_git/r", out _, out _));
    }
}
