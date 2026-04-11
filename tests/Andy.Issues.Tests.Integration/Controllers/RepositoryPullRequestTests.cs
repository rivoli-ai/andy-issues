// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

public class RepositoryPullRequestTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RepositoryPullRequestTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.FakeContainersClient.Reset();
        _factory.FakeGitHubClient.Reset();
        _factory.FakeAzureDevOpsClient.Reset();
    }

    private async Task<(Guid repoId, Guid sandboxId, Guid storyId, string containerId)> SeedGitHubAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "dev-user",
            Name = "example",
            Provider = RepositoryProvider.GitHub,
            CloneUrl = "https://github.com/rivoli-ai/example.git",
            ExternalId = "1234"
        };
        db.Repositories.Add(repo);
        db.LinkedProviders.Add(new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "dev-user",
            Provider = LinkedProviderKind.GitHub,
            AccessToken = "gh-test"
        });
        var sandbox = new Sandbox
        {
            Id = Guid.NewGuid(),
            ContainerId = $"ctr-{Guid.NewGuid():N}",
            RepositoryId = repo.Id,
            OwnerUserId = "dev-user",
            Branch = "feature/x"
        };
        db.Sandboxes.Add(sandbox);
        var epic = new Epic { Id = Guid.NewGuid(), RepositoryId = repo.Id, Title = "E" };
        db.Epics.Add(epic);
        var feature = new Feature { Id = Guid.NewGuid(), EpicId = epic.Id, Title = "F" };
        db.Features.Add(feature);
        var story = new UserStory { Id = Guid.NewGuid(), FeatureId = feature.Id, Title = "S" };
        db.UserStories.Add(story);
        await db.SaveChangesAsync();

        return (repo.Id, sandbox.Id, story.Id, sandbox.ContainerId);
    }

    [Fact]
    public async Task CreatePullRequest_GitHub_ReturnsUrlAndUpdatesStory()
    {
        var (repoId, sandboxId, storyId, containerId) = await SeedGitHubAsync();
        _factory.FakeGitHubClient.PullRequestResult =
            new GitHubPullRequestInfo(99, "https://github.com/rivoli-ai/example/pull/99");

        var req = new CreatePullRequestFromSandboxRequest(
            sandboxId, "Fixup", "Body", "feature/x", "main", storyId);
        var resp = await _client.PostAsJsonAsync($"/api/repositories/{repoId}/pull-request", req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(
            "https://github.com/rivoli-ai/example/pull/99",
            body.RootElement.GetProperty("pullRequestUrl").GetString());

        // git push was executed in the container.
        var execCalls = _factory.FakeContainersClient.ExecCalls;
        Assert.Single(execCalls);
        Assert.Equal(containerId, execCalls[0].containerId);
        Assert.Contains("git", execCalls[0].command);
        Assert.Contains("push", execCalls[0].command);
        Assert.Contains("feature/x", execCalls[0].command);

        // GitHub client was called with parsed owner/repo.
        Assert.Single(_factory.FakeGitHubClient.PullRequestCalls);
        var call = _factory.FakeGitHubClient.PullRequestCalls.Single();
        Assert.Equal("rivoli-ai", call.owner);
        Assert.Equal("example", call.repo);

        // Story PR URL was updated.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var story = await db.UserStories.FindAsync(storyId);
        Assert.Equal("https://github.com/rivoli-ai/example/pull/99", story!.PullRequestUrl);
    }

    [Fact]
    public async Task SandboxConvenience_DerivesRepoAndBranchFromSandbox()
    {
        var (_, sandboxId, storyId, containerId) = await SeedGitHubAsync();
        _factory.FakeGitHubClient.PullRequestResult =
            new GitHubPullRequestInfo(123, "https://github.com/rivoli-ai/example/pull/123");

        var req = new CreateSandboxPullRequestRequest("One-click", "body", storyId);
        var resp = await _client.PostAsJsonAsync($"/api/sandboxes/{sandboxId}/pull-request", req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(
            "https://github.com/rivoli-ai/example/pull/123",
            body.RootElement.GetProperty("pullRequestUrl").GetString());

        // The convenience endpoint must derive source branch from the sandbox row and
        // target branch from the repository's DefaultBranch, so the underlying provider
        // call should match the sandbox's branch even though the request body had no
        // sourceBranch/targetBranch fields.
        var call = Assert.Single(_factory.FakeGitHubClient.PullRequestCalls);
        Assert.Equal("feature/x", call.head);
        Assert.Equal("main", call.baseBranch);

        // git push was executed in the right container.
        Assert.Single(_factory.FakeContainersClient.ExecCalls);
        Assert.Equal(containerId, _factory.FakeContainersClient.ExecCalls[0].containerId);

        // Story PR URL was updated via the shared service path.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var story = await db.UserStories.FindAsync(storyId);
        Assert.Equal("https://github.com/rivoli-ai/example/pull/123", story!.PullRequestUrl);
    }

    [Fact]
    public async Task SandboxConvenience_NotOwner_Returns403()
    {
        // Seed a sandbox owned by someone else.
        Guid sandboxId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var repo = new Repository
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "other",
                Name = "x",
                Provider = RepositoryProvider.GitHub,
                CloneUrl = "https://github.com/x/y.git"
            };
            db.Repositories.Add(repo);
            var sb = new Sandbox
            {
                Id = Guid.NewGuid(),
                ContainerId = "ctr-other",
                RepositoryId = repo.Id,
                OwnerUserId = "other",
                Branch = "main"
            };
            db.Sandboxes.Add(sb);
            await db.SaveChangesAsync();
            sandboxId = sb.Id;
        }

        var resp = await _client.PostAsJsonAsync(
            $"/api/sandboxes/{sandboxId}/pull-request",
            new CreateSandboxPullRequestRequest("t", null, null));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Empty(_factory.FakeContainersClient.ExecCalls);
        Assert.Empty(_factory.FakeGitHubClient.PullRequestCalls);
    }

    [Fact]
    public async Task CreatePullRequest_NonOwner_Returns403()
    {
        // Seed a repo owned by someone else.
        Guid repoId;
        Guid sandboxId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var repo = new Repository
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "someone-else",
                Name = "their",
                Provider = RepositoryProvider.GitHub,
                CloneUrl = "https://github.com/x/y.git"
            };
            db.Repositories.Add(repo);
            var sandbox = new Sandbox
            {
                Id = Guid.NewGuid(),
                ContainerId = "ctr-other",
                RepositoryId = repo.Id,
                OwnerUserId = "someone-else",
                Branch = "main"
            };
            db.Sandboxes.Add(sandbox);
            await db.SaveChangesAsync();
            repoId = repo.Id;
            sandboxId = sandbox.Id;
        }

        var resp = await _client.PostAsJsonAsync(
            $"/api/repositories/{repoId}/pull-request",
            new CreatePullRequestFromSandboxRequest(sandboxId, "t", null, "feature/x", "main", null));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Empty(_factory.FakeContainersClient.ExecCalls);
        Assert.Empty(_factory.FakeGitHubClient.PullRequestCalls);
    }
}
