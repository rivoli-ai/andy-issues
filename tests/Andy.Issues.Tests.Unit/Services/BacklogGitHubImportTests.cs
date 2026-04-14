// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class BacklogGitHubImportTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BacklogGitHubImportTests()
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

    private BacklogGitHubImportService NewImporter(AppDbContext ctx, StubGitHubClient gh) =>
        new(ctx, gh, new RepositoryAccessGuard(ctx), new StubSecretStore(),
            NullLogger<BacklogGitHubImportService>.Instance);

    private async Task<Guid> SeedRepoAsync(string cloneUrl = "https://github.com/acme/widgets.git")
    {
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "widgets",
            Provider = RepositoryProvider.GitHub,
            CloneUrl = cloneUrl
        };
        ctx.Repositories.Add(repo);
        ctx.LinkedProviders.Add(new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Provider = LinkedProviderKind.GitHub,
            AccessToken = "pat"
        });
        await ctx.SaveChangesAsync();
        return repo.Id;
    }

    private static GitHubIssueInfo Issue(
        int number, string title, string type,
        string? body = null, string state = "open", bool isPr = false)
    {
        var labels = type.Length == 0
            ? new List<string>()
            : new List<string> { "type:" + type };
        return new GitHubIssueInfo(number, title, body, state, isPr, labels);
    }

    [Theory]
    [InlineData("https://github.com/acme/widgets.git", "acme", "widgets")]
    [InlineData("https://github.com/acme/widgets", "acme", "widgets")]
    [InlineData("git@github.com:acme/widgets.git", "acme", "widgets")]
    [InlineData("https://dev.azure.com/org/proj/_git/repo", null, null)]
    [InlineData("", null, null)]
    public void TryParseOwnerRepo_VariousForms(string cloneUrl, string? expectedOwner, string? expectedRepo)
    {
        var ok = BacklogGitHubImportService.TryParseOwnerRepo(cloneUrl, out var owner, out var repo);
        if (expectedOwner is null)
        {
            Assert.False(ok);
        }
        else
        {
            Assert.True(ok);
            Assert.Equal(expectedOwner, owner);
            Assert.Equal(expectedRepo, repo);
        }
    }

    [Fact]
    public void ParseTaskListReferences_ExtractsCheckedAndUnchecked()
    {
        const string body = """
            Parent epic body.
            - [ ] #12 first feature
            - [x] #34 done feature
            * [ ] #56 also a feature (asterisk)
            Not a task: #99 (mention in prose)
            - not a checkbox #77
            """;
        var refs = BacklogGitHubImportService.ParseTaskListReferences(body).ToList();
        Assert.Equal(new[] { 12, 34, 56 }, refs);
    }

    [Fact]
    public void ParseTaskListReferences_EmptyOrNull_ReturnsNothing()
    {
        Assert.Empty(BacklogGitHubImportService.ParseTaskListReferences(null));
        Assert.Empty(BacklogGitHubImportService.ParseTaskListReferences(""));
    }

    [Theory]
    [InlineData("open", UserStoryStatus.Draft)]
    [InlineData("OPEN", UserStoryStatus.Draft)]
    [InlineData("closed", UserStoryStatus.Done)]
    [InlineData("CLOSED", UserStoryStatus.Done)]
    public void StatusFromIssueState_Maps(string state, UserStoryStatus expected)
    {
        Assert.Equal(expected, BacklogGitHubImportService.StatusFromIssueState(state));
    }

    [Fact]
    public async Task Import_ClassifiesByLabelAndSkipsUnlabeled()
    {
        var repoId = await SeedRepoAsync();
        var gh = new StubGitHubClient().IssuesFor("acme", "widgets", new[]
        {
            Issue(1, "Epic A", "epic"),
            Issue(2, "Feature B", "feature"),
            Issue(3, "Story C", "story"),
            Issue(4, "Bug report", "") // no type label → skipped
        });

        await using var ctx = NewContext();
        var result = await NewImporter(ctx, gh).ImportAsync(repoId, "alice");

        Assert.NotNull(result);
        // 3 typed + the synthetic Uncategorized epic + Uncategorized feature.
        Assert.Equal(3, result!.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(result.Errors);

        // Epic + Uncategorized.
        Assert.Equal(2, await ctx.Epics.CountAsync());
        // Feature + Uncategorized.
        Assert.Equal(2, await ctx.Features.CountAsync());
        Assert.Single(await ctx.UserStories.ToListAsync());
    }

    [Fact]
    public async Task Import_PullRequestsSkipped()
    {
        var repoId = await SeedRepoAsync();
        var gh = new StubGitHubClient().IssuesFor("acme", "widgets", new[]
        {
            Issue(1, "Epic A", "epic"),
            new GitHubIssueInfo(2, "PR to ignore", null, "open", IsPullRequest: true,
                Labels: new[] { "type:story" })
        });

        await using var ctx = NewContext();
        var result = await NewImporter(ctx, gh).ImportAsync(repoId, "alice");
        Assert.NotNull(result);
        Assert.Equal(0, await ctx.UserStories.CountAsync());
        Assert.Equal(1, result!.Skipped);
    }

    [Fact]
    public async Task Import_InfersHierarchyFromTaskLists()
    {
        var repoId = await SeedRepoAsync();
        var gh = new StubGitHubClient().IssuesFor("acme", "widgets", new[]
        {
            Issue(10, "Epic Alpha", "epic", body: "Features:\n- [ ] #20\n- [ ] #21"),
            Issue(20, "Feature One", "feature", body: "Stories:\n- [ ] #30"),
            Issue(21, "Feature Two", "feature"),
            Issue(30, "Story X", "story"),
            Issue(40, "Orphan story", "story")
        });

        await using var ctx = NewContext();
        var result = await NewImporter(ctx, gh).ImportAsync(repoId, "alice");
        Assert.NotNull(result);

        var epic = await ctx.Epics.FirstAsync(e => e.ExternalId == "gh:10");
        var uncategorizedEpic = await ctx.Epics.FirstAsync(e => e.ExternalId == "gh:uncategorized");
        var uncategorizedFeature = await ctx.Features.FirstAsync(f => f.ExternalId == "gh:uncategorized");

        var f20 = await ctx.Features.FirstAsync(f => f.ExternalId == "gh:20");
        Assert.Equal(epic.Id, f20.EpicId);
        var f21 = await ctx.Features.FirstAsync(f => f.ExternalId == "gh:21");
        Assert.Equal(epic.Id, f21.EpicId);

        var s30 = await ctx.UserStories.FirstAsync(s => s.ExternalId == "gh:30");
        Assert.Equal(f20.Id, s30.FeatureId);

        // Orphan story → uncategorized feature.
        var s40 = await ctx.UserStories.FirstAsync(s => s.ExternalId == "gh:40");
        Assert.Equal(uncategorizedFeature.Id, s40.FeatureId);
        Assert.Equal(uncategorizedEpic.Id, uncategorizedFeature.EpicId);
    }

    [Fact]
    public async Task Import_ClosedIssue_MarkedDone()
    {
        var repoId = await SeedRepoAsync();
        var gh = new StubGitHubClient().IssuesFor("acme", "widgets", new[]
        {
            Issue(1, "Done story", "story", state: "closed")
        });

        await using var ctx = NewContext();
        await NewImporter(ctx, gh).ImportAsync(repoId, "alice");

        var story = await ctx.UserStories.FirstAsync(s => s.ExternalId == "gh:1");
        Assert.Equal(UserStoryStatus.Done, story.Status);
    }

    [Fact]
    public async Task Import_Idempotent_SecondRunUpdatesNotDuplicates()
    {
        var repoId = await SeedRepoAsync();
        var gh = new StubGitHubClient().IssuesFor("acme", "widgets", new[]
        {
            Issue(1, "First title", "story", body: "first body")
        });

        await using (var ctx = NewContext())
        {
            var r1 = await NewImporter(ctx, gh).ImportAsync(repoId, "alice");
            Assert.Equal(1, r1!.Added);
        }

        // Second run with updated title / body should update, not add.
        gh = new StubGitHubClient().IssuesFor("acme", "widgets", new[]
        {
            Issue(1, "Revised title", "story", body: "revised body")
        });
        await using (var ctx = NewContext())
        {
            var r2 = await NewImporter(ctx, gh).ImportAsync(repoId, "alice");
            Assert.Equal(0, r2!.Added);
            Assert.Equal(1, r2.Updated);
        }

        await using (var ctx = NewContext())
        {
            var stories = await ctx.UserStories.ToListAsync();
            Assert.Single(stories);
            Assert.Equal("Revised title", stories[0].Title);
            Assert.Equal("revised body", stories[0].Description);
        }
    }

    [Fact]
    public async Task Import_NonGitHubRepo_ReturnsError()
    {
        var repoId = await SeedRepoAsync(cloneUrl: "https://dev.azure.com/org/proj/_git/repo");
        // Force provider back to AzureDevOps to simulate a wrongly-typed repo.
        await using (var ctx = NewContext())
        {
            var repo = await ctx.Repositories.FindAsync(repoId);
            repo!.Provider = RepositoryProvider.AzureDevOps;
            await ctx.SaveChangesAsync();
        }

        var gh = new StubGitHubClient();
        await using var ctx2 = NewContext();
        var result = await NewImporter(ctx2, gh).ImportAsync(repoId, "alice");
        Assert.NotNull(result);
        Assert.Contains(result!.Errors, e => e.Contains("not linked to GitHub", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Import_NoLinkedProvider_StillAttemptsPublicFetch()
    {
        // Public repos are readable anonymously (60 req/hr/IP). With no
        // LinkedProvider the importer now passes an empty token through
        // and proceeds; the stub doesn't check auth so classification
        // runs as usual.
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "widgets",
            Provider = RepositoryProvider.GitHub,
            CloneUrl = "https://github.com/acme/widgets.git"
        };
        ctx.Repositories.Add(repo);
        await ctx.SaveChangesAsync();

        var gh = new StubGitHubClient().IssuesFor("acme", "widgets", new[]
        {
            Issue(1, "Public story", "story")
        });
        var result = await NewImporter(ctx, gh).ImportAsync(repo.Id, "alice");
        Assert.NotNull(result);
        Assert.Equal(1, result!.Added);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Import_NoLinkedProviderAnd404_SurfacesHelpfulMessage()
    {
        var repoId = await SeedRepoWithoutProviderAsync();
        var gh = new StubGitHubClient();
        gh.ListIssuesException = new GitHubApiException(
            "Repository 'acme/widgets' is not publicly accessible — link a GitHub PAT.",
            statusCode: 404);

        await using var ctx = NewContext();
        var result = await NewImporter(ctx, gh).ImportAsync(repoId, "alice");
        Assert.NotNull(result);
        Assert.Single(result!.Errors);
        Assert.Contains("publicly accessible", result.Errors[0]);
    }

    [Fact]
    public async Task Import_RateLimitError_SurfacesHint()
    {
        var repoId = await SeedRepoAsync();
        var gh = new StubGitHubClient();
        gh.ListIssuesException = new GitHubApiException(
            "GitHub rate limit reached (60/hr unauthenticated). Link a PAT for 5000/hr.",
            statusCode: 403);

        await using var ctx = NewContext();
        var result = await NewImporter(ctx, gh).ImportAsync(repoId, "alice");
        Assert.NotNull(result);
        Assert.Contains("rate limit", result!.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> SeedRepoWithoutProviderAsync(string cloneUrl = "https://github.com/acme/widgets.git")
    {
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "widgets",
            Provider = RepositoryProvider.GitHub,
            CloneUrl = cloneUrl
        };
        ctx.Repositories.Add(repo);
        await ctx.SaveChangesAsync();
        return repo.Id;
    }

    [Fact]
    public async Task Import_Stranger_ReturnsNull()
    {
        var repoId = await SeedRepoAsync();
        var gh = new StubGitHubClient();
        await using var ctx = NewContext();
        var result = await NewImporter(ctx, gh).ImportAsync(repoId, "mallory");
        Assert.Null(result);
    }

    [Fact]
    public async Task Import_UnparseableCloneUrl_ReturnsError()
    {
        var repoId = await SeedRepoAsync(cloneUrl: "not-a-valid-url");
        var gh = new StubGitHubClient();
        await using var ctx = NewContext();
        var result = await NewImporter(ctx, gh).ImportAsync(repoId, "alice");
        Assert.NotNull(result);
        Assert.Contains(result!.Errors, e => e.Contains("derive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Import_FeatureParentedByFirstEpicWhenListedInMultiple()
    {
        // If a feature is referenced by two different epics, the first
        // one wins — matches the "first match wins" rule in the service.
        var repoId = await SeedRepoAsync();
        var gh = new StubGitHubClient().IssuesFor("acme", "widgets", new[]
        {
            Issue(10, "Epic First", "epic", body: "- [ ] #50"),
            Issue(11, "Epic Second", "epic", body: "- [ ] #50"),
            Issue(50, "Contested feature", "feature")
        });

        await using var ctx = NewContext();
        await NewImporter(ctx, gh).ImportAsync(repoId, "alice");

        var firstEpic = await ctx.Epics.FirstAsync(e => e.ExternalId == "gh:10");
        var feature = await ctx.Features.FirstAsync(f => f.ExternalId == "gh:50");
        Assert.Equal(firstEpic.Id, feature.EpicId);
    }
}
