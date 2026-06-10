// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
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

public class BacklogRecategorizeTests : IDisposable
{
    private const string Uncat = "gh:uncategorized";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private long _seq = 1000; // high so the service's allocator (1, 2, …) can't collide

    public BacklogRecategorizeTests()
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

    // MARK: - Setup helpers

    private BacklogRecategorizeService NewService(
        AppDbContext ctx,
        StubGitHubClient gh,
        IHttpClientFactory llm,
        Func<string, string?>? environmentReader = null) =>
        new(ctx, gh, new RepositoryAccessGuard(ctx), new StubSecretStore(),
            new BacklogSequenceAllocator(ctx), llm,
            NullLogger<BacklogRecategorizeService>.Instance,
            environmentReader ?? (_ => null));

    private sealed record Seeded(
        Guid RepoId,
        Guid UncatEpicId,
        Guid UncatFeatureId,
        Guid RealEpicId,
        Guid RealFeatureId);

    /// <summary>
    /// Seeds a GitHub repo (owner "alice") with an Uncategorized
    /// epic+feature pair and, when requested, a real epic (gh:32) with
    /// a real feature (gh:45) under it.
    /// </summary>
    private async Task<Seeded> SeedRepoAsync(
        bool withLlm = true,
        bool withLinkedProvider = true,
        bool withRealParents = true)
    {
        await using var ctx = NewContext();

        LlmSetting? llm = null;
        if (withLlm)
        {
            llm = new LlmSetting
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "alice",
                Name = "test-llm",
                Provider = LlmProvider.OpenAI,
                ApiKey = "sk-test",
                Model = "gpt-4o"
            };
            ctx.LlmSettings.Add(llm);
        }

        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = $"widgets-{Guid.NewGuid():N}",
            Provider = RepositoryProvider.GitHub,
            CloneUrl = "https://github.com/acme/widgets.git",
            LlmSettingId = llm?.Id
        };
        ctx.Repositories.Add(repo);

        if (withLinkedProvider
            && !await ctx.LinkedProviders.AnyAsync(
                p => p.OwnerUserId == "alice" && p.Provider == LinkedProviderKind.GitHub))
        {
            ctx.LinkedProviders.Add(new LinkedProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "alice",
                Provider = LinkedProviderKind.GitHub,
                AccessToken = "pat"
            });
        }

        var uncatEpic = new Epic
        {
            Id = Guid.NewGuid(),
            Seq = ++_seq,
            RepositoryId = repo.Id,
            Title = "Uncategorized",
            Order = int.MaxValue,
            ExternalId = Uncat
        };
        ctx.Epics.Add(uncatEpic);

        var uncatFeature = new Feature
        {
            Id = Guid.NewGuid(),
            Seq = ++_seq,
            EpicId = uncatEpic.Id,
            Title = "Uncategorized",
            Order = int.MaxValue,
            ExternalId = Uncat
        };
        ctx.Features.Add(uncatFeature);

        Guid realEpicId = Guid.Empty, realFeatureId = Guid.Empty;
        if (withRealParents)
        {
            var realEpic = new Epic
            {
                Id = Guid.NewGuid(),
                Seq = ++_seq,
                RepositoryId = repo.Id,
                Title = "Platform Hardening",
                Order = 32,
                ExternalId = "gh:32",
                Labels = new List<string> { "type:epic" }
            };
            ctx.Epics.Add(realEpic);
            var realFeature = new Feature
            {
                Id = Guid.NewGuid(),
                Seq = ++_seq,
                EpicId = realEpic.Id,
                Title = "Auth Flow",
                Order = 45,
                ExternalId = "gh:45",
                Labels = new List<string> { "type:feature" }
            };
            ctx.Features.Add(realFeature);
            realEpicId = realEpic.Id;
            realFeatureId = realFeature.Id;
        }

        await ctx.SaveChangesAsync();
        return new Seeded(repo.Id, uncatEpic.Id, uncatFeature.Id, realEpicId, realFeatureId);
    }

    private async Task<Guid> SeedUncatStoryAsync(
        Seeded seeded, int? ghNumber, string title, IEnumerable<string>? labels = null)
    {
        await using var ctx = NewContext();
        var story = new UserStory
        {
            Id = Guid.NewGuid(),
            Seq = ++_seq,
            FeatureId = seeded.UncatFeatureId,
            Title = title,
            Description = $"Body of {title}",
            Order = ghNumber ?? 0,
            ExternalId = ghNumber is int n ? $"gh:{n}" : null,
            Labels = labels?.ToList() ?? new List<string>()
        };
        ctx.UserStories.Add(story);
        await ctx.SaveChangesAsync();
        return story.Id;
    }

    private async Task<Guid> SeedUncatFeatureAsync(
        Seeded seeded, int? ghNumber, string title)
    {
        await using var ctx = NewContext();
        var feature = new Feature
        {
            Id = Guid.NewGuid(),
            Seq = ++_seq,
            EpicId = seeded.UncatEpicId,
            Title = title,
            Description = $"Body of {title}",
            Order = ghNumber ?? 0,
            ExternalId = ghNumber is int n ? $"gh:{n}" : null
        };
        ctx.Features.Add(feature);
        await ctx.SaveChangesAsync();
        return feature.Id;
    }

    // MARK: - LLM HTTP stubs

    private static IHttpClientFactory LlmReturning(string assistantContent) =>
        new SingleClientFactory(new HttpClient(new LlmStubHandler(HttpStatusCode.OK, assistantContent)));

    private static IHttpClientFactory LlmFailing(HttpStatusCode status) =>
        new SingleClientFactory(new HttpClient(new LlmStubHandler(status, "boom")));

    /// <summary>
    /// Simulates the named HttpClient's own timeout: HttpClient wraps
    /// it in a <see cref="TaskCanceledException"/> even though the
    /// CALLER's token was never cancelled. The service must classify
    /// this as <see cref="RecategorizeOutcome.LlmCallFailed"/>, not
    /// leak it as an unhandled 500 (live regression, 2026-06-10:
    /// gemma4 on x86 exceeded the 100 s default timeout).
    /// </summary>
    private static IHttpClientFactory LlmTimingOut() =>
        new SingleClientFactory(new HttpClient(new LlmTimeoutHandler()));

    private sealed class LlmTimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException(
                "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.",
                new TimeoutException("The operation was canceled."));
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class LlmStubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _content;

        public LlmStubHandler(HttpStatusCode status, string content)
        {
            _status = status;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_status != HttpStatusCode.OK)
                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new StringContent("upstream error")
                });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    choices = new[]
                    {
                        new { message = new { role = "assistant", content = _content } }
                    }
                })
            });
        }
    }

    // MARK: - Local classification

    [Fact]
    public async Task Recategorize_AssignsStoryToExistingFeature_ByGhRef()
    {
        var seeded = await SeedRepoAsync();
        var storyId = await SeedUncatStoryAsync(seeded, 12, "Login button");

        var llm = LlmReturning("""
            { "epics": [], "features": [], "assignments": [
                { "item": "gh:12", "role": "story", "parentRef": "existing:45" } ] }
            """);
        await using var ctx = NewContext();
        var result = await NewService(ctx, new StubGitHubClient(), llm)
            .RecategorizeAsync(seeded.RepoId, "alice", applyToGitHub: false);

        Assert.NotNull(result);
        Assert.Equal(RecategorizeOutcome.Recategorized, result!.Outcome);
        Assert.Equal(1, result.Classified);
        Assert.Equal(1, result.StoriesReparented);
        Assert.Equal(0, result.EpicsCreated);
        Assert.Equal(0, result.FeaturesCreated);
        Assert.Empty(result.Errors);

        await using var verify = NewContext();
        var story = await verify.UserStories.SingleAsync(s => s.Id == storyId);
        Assert.Equal(seeded.RealFeatureId, story.FeatureId);
        Assert.Equal("gh:12", story.ExternalId);
    }

    [Fact]
    public async Task Recategorize_CreatesNewEpicFeatureChain_AndParentsItems()
    {
        var seeded = await SeedRepoAsync(withRealParents: false);
        var story12 = await SeedUncatStoryAsync(seeded, 12, "Search by name");
        var story13 = await SeedUncatStoryAsync(seeded, 13, "Search by tag");

        var llm = LlmReturning("""
            {
              "epics": [ { "ref": "new:E1", "title": "Search", "description": "Search epic" } ],
              "features": [ { "ref": "new:F1", "title": "Query Engine", "description": "Core search", "epicRef": "new:E1" } ],
              "assignments": [
                { "item": "gh:12", "role": "story", "parentRef": "new:F1" },
                { "item": "gh:13", "role": "story", "parentRef": "new:F1" }
              ]
            }
            """);
        await using var ctx = NewContext();
        var result = await NewService(ctx, new StubGitHubClient(), llm)
            .RecategorizeAsync(seeded.RepoId, "alice", applyToGitHub: false);

        Assert.NotNull(result);
        Assert.Equal(RecategorizeOutcome.Recategorized, result!.Outcome);
        Assert.Equal(2, result.Classified);
        Assert.Equal(1, result.EpicsCreated);
        Assert.Equal(1, result.FeaturesCreated);
        Assert.Equal(2, result.StoriesReparented);
        Assert.Empty(result.Errors);

        await using var verify = NewContext();
        var epic = await verify.Epics
            .Include(e => e.Features).ThenInclude(f => f.Stories)
            .SingleAsync(e => e.RepositoryId == seeded.RepoId && e.Title == "Search");
        Assert.Null(epic.ExternalId); // no GitHub write-back requested
        var feature = Assert.Single(epic.Features);
        Assert.Equal("Query Engine", feature.Title);
        Assert.Null(feature.ExternalId);
        Assert.Equal(
            new[] { story12, story13 }.OrderBy(g => g),
            feature.Stories.Select(s => s.Id).OrderBy(g => g));
    }

    [Fact]
    public async Task Recategorize_ReclassifiesStoryRowIntoFeature_PreservingExternalId()
    {
        var seeded = await SeedRepoAsync();
        await SeedUncatStoryAsync(seeded, 14, "Reporting pipeline", labels: new[] { "needs-triage" });

        var llm = LlmReturning("""
            { "assignments": [
                { "item": "gh:14", "role": "feature", "parentRef": "existing:32" } ] }
            """);
        await using var ctx = NewContext();
        var result = await NewService(ctx, new StubGitHubClient(), llm)
            .RecategorizeAsync(seeded.RepoId, "alice", applyToGitHub: false);

        Assert.NotNull(result);
        Assert.Equal(RecategorizeOutcome.Recategorized, result!.Outcome);
        Assert.Equal(1, result.Classified);
        Assert.Equal(0, result.StoriesReparented); // converted, not reparented as a story
        Assert.Equal(0, result.FeaturesCreated);   // conversion, not a new LLM-invented feature
        Assert.Empty(result.Errors);

        await using var verify = NewContext();
        Assert.False(await verify.UserStories.AnyAsync(
            s => s.ExternalId == "gh:14" && s.Feature.Epic.RepositoryId == seeded.RepoId));
        var converted = await verify.Features.SingleAsync(
            f => f.ExternalId == "gh:14" && f.Epic.RepositoryId == seeded.RepoId);
        Assert.Equal(seeded.RealEpicId, converted.EpicId);
        Assert.Equal("Reporting pipeline", converted.Title);
        Assert.Contains("needs-triage", converted.Labels);
    }

    [Fact]
    public async Task Recategorize_UnknownParentRef_RecordsErrorAndAppliesOthers()
    {
        var seeded = await SeedRepoAsync();
        var story12 = await SeedUncatStoryAsync(seeded, 12, "Good story");
        var story13 = await SeedUncatStoryAsync(seeded, 13, "Orphaned story");

        var llm = LlmReturning("""
            { "assignments": [
                { "item": "gh:12", "role": "story", "parentRef": "existing:45" },
                { "item": "gh:13", "role": "story", "parentRef": "existing:999" } ] }
            """);
        await using var ctx = NewContext();
        var result = await NewService(ctx, new StubGitHubClient(), llm)
            .RecategorizeAsync(seeded.RepoId, "alice", applyToGitHub: false);

        Assert.NotNull(result);
        Assert.Equal(RecategorizeOutcome.Recategorized, result!.Outcome);
        Assert.Equal(1, result.Classified);
        Assert.Equal(1, result.StoriesReparented);
        var error = Assert.Single(result.Errors);
        Assert.Contains("gh:13", error);
        Assert.Contains("existing:999", error);

        await using var verify = NewContext();
        Assert.Equal(seeded.RealFeatureId,
            (await verify.UserStories.SingleAsync(s => s.Id == story12)).FeatureId);
        // The unplaced story stays in the (non-empty, thus preserved) bucket.
        Assert.Equal(seeded.UncatFeatureId,
            (await verify.UserStories.SingleAsync(s => s.Id == story13)).FeatureId);
        Assert.True(await verify.Epics.AnyAsync(
            e => e.RepositoryId == seeded.RepoId && e.ExternalId == Uncat));
    }

    [Fact]
    public async Task Recategorize_NoLlmSetting_ReturnsNoLlmSetting()
    {
        var seeded = await SeedRepoAsync(withLlm: false);
        await SeedUncatStoryAsync(seeded, 12, "Some story");

        await using var ctx = NewContext();
        var result = await NewService(ctx, new StubGitHubClient(), LlmReturning("{}"))
            .RecategorizeAsync(seeded.RepoId, "alice", applyToGitHub: false);

        Assert.NotNull(result);
        Assert.Equal(RecategorizeOutcome.NoLlmSetting, result!.Outcome);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task Recategorize_LlmHttpFailure_ReturnsLlmCallFailed()
    {
        var seeded = await SeedRepoAsync();
        await SeedUncatStoryAsync(seeded, 12, "Some story");

        await using var ctx = NewContext();
        var result = await NewService(ctx, new StubGitHubClient(), LlmFailing(HttpStatusCode.InternalServerError))
            .RecategorizeAsync(seeded.RepoId, "alice", applyToGitHub: false);

        Assert.NotNull(result);
        Assert.Equal(RecategorizeOutcome.LlmCallFailed, result!.Outcome);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task Recategorize_LlmClientTimeout_ReturnsLlmCallFailed_NotUnhandled()
    {
        var seeded = await SeedRepoAsync();
        await SeedUncatStoryAsync(seeded, 12, "Some story");

        await using var ctx = NewContext();
        // Caller token NOT cancelled — only the HttpClient timed out.
        var result = await NewService(ctx, new StubGitHubClient(), LlmTimingOut())
            .RecategorizeAsync(seeded.RepoId, "alice", applyToGitHub: false);

        Assert.NotNull(result);
        Assert.Equal(RecategorizeOutcome.LlmCallFailed, result!.Outcome);
        Assert.Contains("canceled", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("this is not json at all")]
    [InlineData("""{ "epics": [] }""")] // structurally valid JSON, missing "assignments"
    public async Task Recategorize_UnparseableResponse_ReturnsParseFailed(string llmContent)
    {
        var seeded = await SeedRepoAsync();
        await SeedUncatStoryAsync(seeded, 12, "Some story");

        await using var ctx = NewContext();
        var result = await NewService(ctx, new StubGitHubClient(), LlmReturning(llmContent))
            .RecategorizeAsync(seeded.RepoId, "alice", applyToGitHub: false);

        Assert.NotNull(result);
        Assert.Equal(RecategorizeOutcome.ParseFailed, result!.Outcome);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task Recategorize_UnknownRepo_ReturnsNull()
    {
        await using var ctx = NewContext();
        var result = await NewService(ctx, new StubGitHubClient(), LlmReturning("{}"))
            .RecategorizeAsync(Guid.NewGuid(), "alice", applyToGitHub: false);
        Assert.Null(result);
    }

    [Fact]
    public async Task Recategorize_NoUncategorizedItems_ReturnsNothingToDo()
    {
        var seeded = await SeedRepoAsync(); // buckets exist but are empty

        await using var ctx = NewContext();
        var result = await NewService(ctx, new StubGitHubClient(), LlmReturning("{}"))
            .RecategorizeAsync(seeded.RepoId, "alice", applyToGitHub: false);

        Assert.NotNull(result);
        Assert.Equal(RecategorizeOutcome.NothingToDo, result!.Outcome);
        Assert.Equal(0, result.Classified);
        Assert.Equal(0, result.EpicsCreated);
        Assert.Equal(0, result.FeaturesCreated);
        Assert.Equal(0, result.StoriesReparented);
        Assert.Equal(0, result.LabelsApplied);
        Assert.Equal(0, result.SubIssuesLinked);
        Assert.Equal(0, result.GithubIssuesCreated);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Recategorize_EmptiedUncategorizedBuckets_AreDeleted()
    {
        var seeded = await SeedRepoAsync();
        await SeedUncatStoryAsync(seeded, 12, "Only story");

        var llm = LlmReturning("""
            { "assignments": [
                { "item": "gh:12", "role": "story", "parentRef": "existing:45" } ] }
            """);
        await using var ctx = NewContext();
        var result = await NewService(ctx, new StubGitHubClient(), llm)
            .RecategorizeAsync(seeded.RepoId, "alice", applyToGitHub: false);

        Assert.Equal(RecategorizeOutcome.Recategorized, result!.Outcome);

        await using var verify = NewContext();
        Assert.False(await verify.Features.AnyAsync(
            f => f.Epic.RepositoryId == seeded.RepoId && f.ExternalId == Uncat));
        Assert.False(await verify.Epics.AnyAsync(
            e => e.RepositoryId == seeded.RepoId && e.ExternalId == Uncat));
    }

    // MARK: - GitHub write-back

    [Fact]
    public async Task Recategorize_ApplyToGitHub_RecordsWritesAndStampsExternalIds()
    {
        var seeded = await SeedRepoAsync();
        // gh:12 already carries type:story → label write must be skipped for it.
        await SeedUncatStoryAsync(seeded, 12, "Tag search", labels: new[] { "type:story" });
        await SeedUncatStoryAsync(seeded, 13, "Name search");

        var gh = new StubGitHubClient()
            .IssuesFor("acme", "widgets", new[]
            {
                new GitHubIssueInfo(12, "Tag search", null, "open", false, new List<string> { "type:story" }, Id: 1200),
                new GitHubIssueInfo(13, "Name search", null, "open", false, new List<string>(), Id: 1300),
                new GitHubIssueInfo(32, "Platform Hardening", null, "open", false, new List<string> { "type:epic" }, Id: 3200),
                new GitHubIssueInfo(45, "Auth Flow", null, "open", false, new List<string> { "type:feature" }, Id: 4500)
            });

        var llm = LlmReturning("""
            {
              "epics": [ { "ref": "new:E1", "title": "Search", "description": "Search epic" } ],
              "features": [ { "ref": "new:F1", "title": "Query Engine", "description": "Core", "epicRef": "new:E1" } ],
              "assignments": [
                { "item": "gh:12", "role": "story", "parentRef": "existing:45" },
                { "item": "gh:13", "role": "story", "parentRef": "new:F1" }
              ]
            }
            """);
        await using var ctx = NewContext();
        var result = await NewService(ctx, gh, llm)
            .RecategorizeAsync(seeded.RepoId, "alice", applyToGitHub: true);

        Assert.NotNull(result);
        Assert.Equal(RecategorizeOutcome.Recategorized, result!.Outcome);
        Assert.Empty(result.Errors);

        // Created issues: epic first (#100), then feature (#101).
        Assert.Equal(2, result.GithubIssuesCreated);
        Assert.Collection(gh.CreateIssueCalls,
            c =>
            {
                Assert.Equal(("acme", "widgets", "Search"), (c.owner, c.repo, c.title));
                Assert.Equal(new[] { "type:epic" }, c.labels);
            },
            c =>
            {
                Assert.Equal(("acme", "widgets", "Query Engine"), (c.owner, c.repo, c.title));
                Assert.Equal(new[] { "type:feature" }, c.labels);
            });

        await using var verify = NewContext();
        var newEpic = await verify.Epics.SingleAsync(
            e => e.RepositoryId == seeded.RepoId && e.Title == "Search");
        Assert.Equal("gh:100", newEpic.ExternalId);
        var newFeature = await verify.Features.SingleAsync(
            f => f.Epic.RepositoryId == seeded.RepoId && f.Title == "Query Engine");
        Assert.Equal("gh:101", newFeature.ExternalId);

        // Labels: only gh:13 needed one (gh:12 already had type:story).
        Assert.Equal(1, result.LabelsApplied);
        var labelCall = Assert.Single(gh.AddLabelsCalls);
        Assert.Equal(13, labelCall.issueNumber);
        Assert.Equal(new[] { "type:story" }, labelCall.labels);

        // Sub-issue links: 45←12, 101←13, 100←101 (created feature under created epic).
        Assert.Equal(3, result.SubIssuesLinked);
        var links = gh.AddSubIssueCalls
            .Select(c => (c.parentIssueNumber, c.childIssueId))
            .OrderBy(t => t.parentIssueNumber)
            .ToArray();
        Assert.Equal(new (int, long)[] { (45, 1200), (100, 101_000), (101, 1300) }, links);
    }

    [Fact]
    public async Task Recategorize_ApplyToGitHub_OneWriteFails_RestProceed()
    {
        var seeded = await SeedRepoAsync();
        await SeedUncatStoryAsync(seeded, 12, "Tag search");
        await SeedUncatStoryAsync(seeded, 13, "Name search");

        var gh = new StubGitHubClient()
            .IssuesFor("acme", "widgets", new[]
            {
                new GitHubIssueInfo(12, "Tag search", null, "open", false, new List<string>(), Id: 1200),
                new GitHubIssueInfo(13, "Name search", null, "open", false, new List<string>(), Id: 1300),
                new GitHubIssueInfo(45, "Auth Flow", null, "open", false, new List<string>(), Id: 4500)
            });
        gh.AddLabelsExceptions[12] = new GitHubApiException("GitHub forbade the request — check the PAT's scopes.", 403);

        var llm = LlmReturning("""
            { "assignments": [
                { "item": "gh:12", "role": "story", "parentRef": "existing:45" },
                { "item": "gh:13", "role": "story", "parentRef": "existing:45" } ] }
            """);
        await using var ctx = NewContext();
        var result = await NewService(ctx, gh, llm)
            .RecategorizeAsync(seeded.RepoId, "alice", applyToGitHub: true);

        Assert.NotNull(result);
        Assert.Equal(RecategorizeOutcome.Recategorized, result!.Outcome);
        Assert.Equal(2, result.Classified);

        // gh:12's label write failed → error recorded, everything else proceeded.
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("#12:", error);
        Assert.Equal(1, result.LabelsApplied);
        Assert.Equal(13, Assert.Single(gh.AddLabelsCalls).issueNumber);
        Assert.Equal(2, result.SubIssuesLinked); // both links still made
    }

    [Fact]
    public async Task Recategorize_ApplyToGitHub_NoPat_AppliesLocallyWithWarning()
    {
        var seeded = await SeedRepoAsync(withLinkedProvider: false);
        var storyId = await SeedUncatStoryAsync(seeded, 12, "Tag search");

        var gh = new StubGitHubClient();
        var llm = LlmReturning("""
            { "assignments": [
                { "item": "gh:12", "role": "story", "parentRef": "existing:45" } ] }
            """);
        await using var ctx = NewContext();
        var result = await NewService(ctx, gh, llm, environmentReader: _ => null)
            .RecategorizeAsync(seeded.RepoId, "alice", applyToGitHub: true);

        Assert.NotNull(result);
        Assert.Equal(RecategorizeOutcome.Recategorized, result!.Outcome);
        Assert.Equal(1, result.Classified);
        Assert.Contains("No GitHub credential linked — recategorized locally only.", result.Errors);
        Assert.Equal(0, result.LabelsApplied);
        Assert.Equal(0, result.SubIssuesLinked);
        Assert.Equal(0, result.GithubIssuesCreated);
        Assert.Empty(gh.AddLabelsCalls);
        Assert.Empty(gh.CreateIssueCalls);
        Assert.Empty(gh.AddSubIssueCalls);

        await using var verify = NewContext();
        Assert.Equal(seeded.RealFeatureId,
            (await verify.UserStories.SingleAsync(s => s.Id == storyId)).FeatureId);
    }

    [Fact]
    public async Task Recategorize_ReparentsUncategorizedFeatureToExistingEpic()
    {
        var seeded = await SeedRepoAsync();
        var featureId = await SeedUncatFeatureAsync(seeded, 50, "Billing");

        var llm = LlmReturning("""
            { "assignments": [
                { "item": "gh:50", "role": "feature", "parentRef": "existing:32" } ] }
            """);
        await using var ctx = NewContext();
        var result = await NewService(ctx, new StubGitHubClient(), llm)
            .RecategorizeAsync(seeded.RepoId, "alice", applyToGitHub: false);

        Assert.NotNull(result);
        Assert.Equal(RecategorizeOutcome.Recategorized, result!.Outcome);
        Assert.Equal(1, result.Classified);
        Assert.Equal(0, result.StoriesReparented);
        Assert.Empty(result.Errors);

        await using var verify = NewContext();
        var feature = await verify.Features.SingleAsync(f => f.Id == featureId);
        Assert.Equal(seeded.RealEpicId, feature.EpicId);
    }
}
