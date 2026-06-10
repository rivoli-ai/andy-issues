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
        // 1 epic + 1 feature + 3 stories — the unlabelled bug is now
        // imported as a story rather than skipped (see the widened
        // classifier in BacklogGitHubImportService.ClassifyIssue).
        Assert.Equal(5, result!.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Skipped);
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

    // -------------------------------------------------------------------
    // conductor#670 Bug 2 — end-to-end coverage through the HTTP sync
    // endpoint. First sync establishes the initial classification;
    // second sync with changed labels (or changed GitHub type) must
    // re-home the entity AND update stored labels/type.
    // -------------------------------------------------------------------

    /// <summary>Helper matching the one in the unit-test file; builds
    /// a GitHub issue with arbitrary labels and an optional typed
    /// Issue Type. #670 Bug 2.</summary>
    private static GitHubIssueInfo IssueWithLabels(
        int number,
        string title,
        IEnumerable<string> labels,
        string? type = null,
        string? body = null,
        string state = "open") =>
        new(number, title, body, state, IsPullRequest: false, labels.ToList(), type);

    [Fact]
    public async Task Resync_WhenEpicRelabelledAsStory_RowMovesAndLabelsPersist()
    {
        // First sync classifies issue #100 as an Epic.
        await SeedPatAsync();
        var repoId = await SeedRepoAsync();
        _factory.FakeGitHubClient.SetIssues("acme", "widgets", new[]
        {
            IssueWithLabels(100, "Cross-cutting epic",
                labels: new[] { "type:epic", "priority:high" },
                type: "Feature"),
        });
        var firstResponse = await _client.PostAsync(
            $"/api/repositories/{repoId}/sync-github-issues",
            content: null);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var epic = await db.Epics.FirstAsync(e => e.ExternalId == "gh:100");
            Assert.Equal(new[] { "type:epic", "priority:high" }, epic.Labels);
            Assert.Equal("Feature", epic.GitHubType);
        }

        // Upstream flipped the labels — now it's a Story under the
        // `bug` type. Re-sync must move the row to UserStories and
        // persist the new labels + type.
        _factory.FakeGitHubClient.SetIssues("acme", "widgets", new[]
        {
            IssueWithLabels(100, "Cross-cutting epic",
                labels: new[] { "story", "priority:high" },
                type: "Bug"),
        });
        var secondResponse = await _client.PostAsync(
            $"/api/repositories/{repoId}/sync-github-issues",
            content: null);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        using var check = _factory.Services.CreateScope();
        var db2 = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(await db2.Epics.FirstOrDefaultAsync(e => e.ExternalId == "gh:100"));
        var story = await db2.UserStories.FirstOrDefaultAsync(s => s.ExternalId == "gh:100");
        Assert.NotNull(story);
        Assert.Equal(new[] { "story", "priority:high" }, story!.Labels);
        Assert.Equal("Bug", story.GitHubType);
    }

    [Fact]
    public async Task Post_NativeSubIssues_BuildTree_AndBacklogGetShowsIt()
    {
        // Native sub-issues are the primary hierarchy source: epic #1
        // has sub-issues [2, 10] (a feature and a DIRECT story),
        // feature #2 has sub-issue [3]. No body carries any #N ref —
        // exactly the rivoli-ai/andy-cli shape that used to dump
        // everything into "Uncategorized".
        await SeedPatAsync();
        var repoId = await SeedRepoAsync();
        _factory.FakeGitHubClient.SetIssues("acme", "widgets", new[]
        {
            Issue(1, "Platform epic", "epic",
                body: "- [ ] build it\n- [ ] ship it") with { SubIssuesTotal = 2 },
            Issue(2, "Auth feature", "feature") with { SubIssuesTotal = 1 },
            Issue(3, "Login story", "story"),
            Issue(10, "Direct epic story", "story"),
        });
        _factory.FakeGitHubClient.SetSubIssues("acme", "widgets", 1, new[] { 2, 10 });
        _factory.FakeGitHubClient.SetSubIssues("acme", "widgets", 2, new[] { 3 });

        var response = await _client.PostAsync(
            $"/api/repositories/{repoId}/sync-github-issues",
            content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SyncResult>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(4, result!.Added);
        Assert.Empty(result.Errors);

        // The backlog GET (what Conductor renders) shows the tree.
        var backlog = await _client.GetFromJsonAsync<BacklogDto>(
            $"/api/repositories/{repoId}/backlog", JsonOptions);
        Assert.NotNull(backlog);

        // Exactly one epic — NO synthetic Uncategorized epic.
        var epic = Assert.Single(backlog!.Epics);
        Assert.Equal("gh:1", epic.ExternalId);

        // Feature #2 under the epic, story #3 under the feature.
        Assert.Equal(2, epic.Features.Count);
        var feature = epic.Features.Single(f => f.ExternalId == "gh:2");
        var story = Assert.Single(feature.Stories);
        Assert.Equal("gh:3", story.ExternalId);

        // The direct epic→story link lands in the synthetic per-epic
        // "Stories" feature, not in Uncategorized.
        var storiesFeature = epic.Features.Single(f => f.ExternalId == "gh:1/stories");
        Assert.Equal("Stories", storiesFeature.Title);
        var directStory = Assert.Single(storiesFeature.Stories);
        Assert.Equal("gh:10", directStory.ExternalId);
    }

    [Fact]
    public async Task Resync_TypeFlipWithoutLabelChange_IsStillReflected()
    {
        // GitHub typed Issue Types are independent of labels. When
        // only the type changes upstream, the row stays in place but
        // the `GitHubType` column updates.
        await SeedPatAsync();
        var repoId = await SeedRepoAsync();

        _factory.FakeGitHubClient.SetIssues("acme", "widgets", new[]
        {
            IssueWithLabels(200, "Some story",
                labels: new[] { "story" }, type: "Task"),
        });
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsync(
                $"/api/repositories/{repoId}/sync-github-issues", null)).StatusCode);

        _factory.FakeGitHubClient.SetIssues("acme", "widgets", new[]
        {
            IssueWithLabels(200, "Some story",
                labels: new[] { "story" }, type: "Bug"),
        });
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsync(
                $"/api/repositories/{repoId}/sync-github-issues", null)).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var story = await db.UserStories.FirstAsync(s => s.ExternalId == "gh:200");
        Assert.Equal("Bug", story.GitHubType);
    }
}
