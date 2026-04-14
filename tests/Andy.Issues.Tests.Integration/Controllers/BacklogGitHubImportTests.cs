// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

/// <summary>
/// End-to-end coverage for <c>POST /api/repositories/{id}/sync-github-issues</c>
/// through the real ASP.NET Core pipeline (controller routing, DI,
/// auth, JSON serialization). The unit tests cover the service in
/// isolation; these exercise everything the live Conductor client
/// actually traverses.
/// </summary>
public class BacklogGitHubImportTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BacklogGitHubImportTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.FakeGitHubClient.Reset();
    }

    private async Task<Guid> SeedRepoAsync(
        string cloneUrl = "https://github.com/acme/widgets.git",
        RepositoryProvider provider = RepositoryProvider.GitHub)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "dev-user",
            Name = "widgets",
            Provider = provider,
            CloneUrl = cloneUrl
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        return repo.Id;
    }

    private async Task SeedPatAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.LinkedProviders.Any(p => p.OwnerUserId == "dev-user" && p.Provider == LinkedProviderKind.GitHub))
        {
            db.LinkedProviders.Add(new LinkedProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "dev-user",
                Provider = LinkedProviderKind.GitHub,
                AccessToken = "ghp_test"
            });
            await db.SaveChangesAsync();
        }
    }

    private static GitHubIssueInfo Issue(int number, string title, string type,
        string? body = null, string state = "open")
        => new(number, title, body, state, IsPullRequest: false,
            Labels: type.Length == 0 ? new List<string>() : new[] { "type:" + type });

    [Fact]
    public async Task Post_ReturnsOk_AndImportsClassifiedIssues()
    {
        await SeedPatAsync();
        var repoId = await SeedRepoAsync();
        _factory.FakeGitHubClient.SetIssues("acme", "widgets", new[]
        {
            Issue(1, "Platform epic", "epic", body: "- [ ] #2"),
            Issue(2, "Auth feature", "feature", body: "- [ ] #3\n- [x] #4"),
            Issue(3, "Login story", "story"),
            Issue(4, "Logout story", "story", state: "closed"),
            Issue(5, "Unlabelled bug", "")
        });

        var response = await _client.PostAsync(
            $"/api/repositories/{repoId}/sync-github-issues",
            content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SyncResult>(JsonOptions);
        Assert.NotNull(result);
        // 1 epic + 1 feature + 2 stories created; 1 unlabelled skipped.
        Assert.Equal(4, result!.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(result.Errors);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The imported items plus the synthetic "Uncategorized" epic/feature.
        var epic = await db.Epics.FirstAsync(e => e.ExternalId == "gh:1");
        var feature = await db.Features.FirstAsync(f => f.ExternalId == "gh:2");
        Assert.Equal(epic.Id, feature.EpicId);
        var loginStory = await db.UserStories.FirstAsync(s => s.ExternalId == "gh:3");
        Assert.Equal(feature.Id, loginStory.FeatureId);
        Assert.Equal(UserStoryStatus.Draft, loginStory.Status);
        var logoutStory = await db.UserStories.FirstAsync(s => s.ExternalId == "gh:4");
        Assert.Equal(UserStoryStatus.Done, logoutStory.Status);
    }

    [Fact]
    public async Task Post_NoLinkedProvider_PublicRepo_SucceedsAnonymously()
    {
        // #64: a missing PAT no longer blocks the sync — public repos
        // are readable anonymously. The fake GitHub client doesn't
        // gate on the token value, so classification runs as usual.
        await RemoveLinkedProviderAsync();
        var repoId = await SeedRepoAsync();
        _factory.FakeGitHubClient.SetIssues("acme", "widgets", new[]
        {
            Issue(42, "Public story", "story")
        });

        var response = await _client.PostAsync(
            $"/api/repositories/{repoId}/sync-github-issues",
            content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SyncResult>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(1, result!.Added);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Post_NoLinkedProvider_PrivateRepo_SurfacesHelpfulError()
    {
        // When GitHub returns a 404 (private repo invisible to anon)
        // the client now raises a typed GitHubApiException; the
        // service translates it into a user-facing message.
        await RemoveLinkedProviderAsync();
        var repoId = await SeedRepoAsync();
        _factory.FakeGitHubClient.ListIssuesException = new GitHubApiException(
            "Repository 'acme/widgets' is not publicly accessible — link a GitHub PAT.",
            statusCode: 404);

        var response = await _client.PostAsync(
            $"/api/repositories/{repoId}/sync-github-issues",
            content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SyncResult>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(0, result!.Added);
        Assert.Single(result.Errors);
        Assert.Contains("publicly accessible", result.Errors[0]);
    }

    private async Task RemoveLinkedProviderAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pat = db.LinkedProviders.FirstOrDefault(p =>
            p.OwnerUserId == "dev-user" && p.Provider == LinkedProviderKind.GitHub);
        if (pat is not null)
        {
            db.LinkedProviders.Remove(pat);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Post_UnknownRepository_Returns404()
    {
        var response = await _client.PostAsync(
            $"/api/repositories/{Guid.NewGuid()}/sync-github-issues",
            content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_IdempotentSecondCall_UpdatesNotDuplicates()
    {
        await SeedPatAsync();
        var repoId = await SeedRepoAsync();
        _factory.FakeGitHubClient.SetIssues("acme", "widgets", new[]
        {
            Issue(7, "First title", "story", body: "first body")
        });

        var first = await _client.PostAsync(
            $"/api/repositories/{repoId}/sync-github-issues",
            content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        _factory.FakeGitHubClient.SetIssues("acme", "widgets", new[]
        {
            Issue(7, "Revised title", "story", body: "revised body")
        });
        var second = await _client.PostAsync(
            $"/api/repositories/{repoId}/sync-github-issues",
            content: null);

        var result = await second.Content.ReadFromJsonAsync<SyncResult>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(0, result!.Added);
        Assert.Equal(1, result.Updated);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stories = db.UserStories.Where(s => s.ExternalId == "gh:7").ToList();
        Assert.Single(stories);
        Assert.Equal("Revised title", stories[0].Title);
    }
}
