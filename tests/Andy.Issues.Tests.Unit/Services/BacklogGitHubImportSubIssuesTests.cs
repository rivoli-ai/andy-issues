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

/// <summary>
/// Hierarchy inference from GitHub NATIVE sub-issues (primary source)
/// with task-list parsing as fallback, plus the lazy/self-healing
/// Uncategorized bucket lifecycle. Real repos (e.g. rivoli-ai/andy-cli)
/// use native sub-issues and have task lists WITHOUT <c>#N</c> refs —
/// before this slice everything landed in "Uncategorized".
/// </summary>
public class BacklogGitHubImportSubIssuesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BacklogGitHubImportSubIssuesTests()
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
            new BacklogSequenceAllocator(ctx),
            NullLogger<BacklogGitHubImportService>.Instance);

    private async Task<Guid> SeedRepoAsync(string cloneUrl = "https://github.com/acme/widgets.git")
    {
        await using var ctx = NewContext();
        BacklogGitHubImportService.TryParseOwnerRepo(cloneUrl, out _, out var repoName);
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = string.IsNullOrEmpty(repoName) ? "widgets" : repoName,
            Provider = RepositoryProvider.GitHub,
            CloneUrl = cloneUrl
        };
        ctx.Repositories.Add(repo);
        var hasProvider = await ctx.LinkedProviders.AnyAsync(
            p => p.OwnerUserId == "alice" && p.Provider == LinkedProviderKind.GitHub);
        if (!hasProvider)
        {
            ctx.LinkedProviders.Add(new LinkedProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "alice",
                Provider = LinkedProviderKind.GitHub,
                AccessToken = "pat"
            });
        }
        await ctx.SaveChangesAsync();
        return repo.Id;
    }

    private static GitHubIssueInfo Issue(
        int number, string title, string type,
        string? body = null, string state = "open",
        int subIssuesTotal = 0)
    {
        var labels = type.Length == 0
            ? new List<string>()
            : new List<string> { "type:" + type };
        return new GitHubIssueInfo(
            number, title, body, state, IsPullRequest: false, labels,
            SubIssuesTotal: subIssuesTotal);
    }

    // ── 1. Native sub-issues build the tree (no task-list refs at all) ──

    [Fact]
    public async Task Import_NativeSubIssues_BuildTree_AndNoUncategorizedRows()
    {
        // andy-cli pattern: epic #1 → sub-issues [2]; feature #2 →
        // sub-issues [3]. Bodies carry task lists WITHOUT any #N refs.
        var repoId = await SeedRepoAsync();
        var gh = new StubGitHubClient()
            .IssuesFor("acme", "widgets", new[]
            {
                Issue(1, "Epic", "epic",
                    body: "- [ ] Build the binary pipeline\n- [ ] Ship docs",
                    subIssuesTotal: 1),
                Issue(2, "Feature", "feature",
                    body: "- [x] Wire the parser",
                    subIssuesTotal: 1),
                Issue(3, "Story", "story")
            })
            .SubIssuesFor("acme", "widgets", 1, new[] { 2 })
            .SubIssuesFor("acme", "widgets", 2, new[] { 3 });

        await using var ctx = NewContext();
        var result = await NewImporter(ctx, gh).ImportAsync(repoId, "alice");

        Assert.NotNull(result);
        Assert.Equal(3, result!.Added);
        Assert.Empty(result.Errors);

        var epic = await ctx.Epics.FirstAsync(e => e.ExternalId == "gh:1");
        var feature = await ctx.Features.FirstAsync(f => f.ExternalId == "gh:2");
        Assert.Equal(epic.Id, feature.EpicId);
        var story = await ctx.UserStories.FirstAsync(s => s.ExternalId == "gh:3");
        Assert.Equal(feature.Id, story.FeatureId);

        // Fully categorized — NO Uncategorized epic/feature may exist.
        Assert.Null(await ctx.Epics.FirstOrDefaultAsync(
            e => e.ExternalId == "gh:uncategorized"));
        Assert.Null(await ctx.Features.FirstOrDefaultAsync(
            f => f.ExternalId == "gh:uncategorized"));
    }

    // ── 2. Trailing-ref task lists (conductor epic style) ───────────────

    [Fact]
    public async Task Import_TrailingRefTaskList_ParentsFeature()
    {
        // Conductor-style epics put the ref at the END of the line.
        var repoId = await SeedRepoAsync();
        var gh = new StubGitHubClient().IssuesFor("acme", "widgets", new[]
        {
            Issue(1, "Epic", "epic",
                body: "- [ ] **AN1** Binary build pipeline (#7)"),
            Issue(7, "Binary build pipeline", "feature")
        });

        await using var ctx = NewContext();
        await NewImporter(ctx, gh).ImportAsync(repoId, "alice");

        var epic = await ctx.Epics.FirstAsync(e => e.ExternalId == "gh:1");
        var feature = await ctx.Features.FirstAsync(f => f.ExternalId == "gh:7");
        Assert.Equal(epic.Id, feature.EpicId);
    }

    // ── 3. Same-repo issue-URL refs ─────────────────────────────────────

    [Fact]
    public async Task Import_UrlFormRef_ParentsFeature_WhenOwnerRepoKnown()
    {
        // The repo IS rivoli-ai/demo, so its full issue URLs count as
        // same-repo refs.
        var repoId = await SeedRepoAsync(cloneUrl: "https://github.com/rivoli-ai/demo.git");
        var gh = new StubGitHubClient().IssuesFor("rivoli-ai", "demo", new[]
        {
            Issue(1, "Epic", "epic",
                body: "- [ ] https://github.com/rivoli-ai/demo/issues/9"),
            Issue(9, "Linked feature", "feature")
        });

        await using var ctx = NewContext();
        await NewImporter(ctx, gh).ImportAsync(repoId, "alice");

        var epic = await ctx.Epics.FirstAsync(e => e.ExternalId == "gh:1");
        var feature = await ctx.Features.FirstAsync(f => f.ExternalId == "gh:9");
        Assert.Equal(epic.Id, feature.EpicId);
    }

    [Fact]
    public void ParseTaskListReferences_UrlForm_RequiresOwnerRepo()
    {
        const string body = "- [ ] https://github.com/rivoli-ai/demo/issues/9";

        // With owner/repo (case-insensitive) the URL resolves.
        Assert.Equal(new[] { 9 }, BacklogGitHubImportService
            .ParseTaskListReferences(body, "rivoli-ai", "demo").ToList());
        Assert.Equal(new[] { 9 }, BacklogGitHubImportService
            .ParseTaskListReferences(body, "Rivoli-AI", "Demo").ToList());

        // Without owner/repo — or with a different repo — it does NOT.
        Assert.Empty(BacklogGitHubImportService.ParseTaskListReferences(body));
        Assert.Empty(BacklogGitHubImportService
            .ParseTaskListReferences(body, "acme", "widgets"));
    }

    // ── 4. Native sub-issues take precedence over task-list refs ────────

    [Fact]
    public async Task Import_NativeSubIssues_TakePrecedenceOverConflictingTaskListRef()
    {
        // Feature #2's NATIVE sub-issues claim story #3 while the
        // epic's task list also references #3 directly (which alone
        // would route it to the synthetic per-epic Stories feature).
        // The feature linkage must win and no synthetic Stories
        // feature may remain.
        var repoId = await SeedRepoAsync();
        var gh = new StubGitHubClient()
            .IssuesFor("acme", "widgets", new[]
            {
                Issue(1, "Epic", "epic",
                    body: "- [ ] #3",
                    subIssuesTotal: 1),
                Issue(2, "Feature", "feature", subIssuesTotal: 1),
                Issue(3, "Story", "story")
            })
            .SubIssuesFor("acme", "widgets", 1, new[] { 2 })
            .SubIssuesFor("acme", "widgets", 2, new[] { 3 });

        await using var ctx = NewContext();
        await NewImporter(ctx, gh).ImportAsync(repoId, "alice");

        var feature = await ctx.Features.FirstAsync(f => f.ExternalId == "gh:2");
        var story = await ctx.UserStories.FirstAsync(s => s.ExternalId == "gh:3");
        Assert.Equal(feature.Id, story.FeatureId);

        Assert.Null(await ctx.Features.FirstOrDefaultAsync(
            f => f.ExternalId == "gh:1/stories"));
        Assert.Null(await ctx.Epics.FirstOrDefaultAsync(
            e => e.ExternalId == "gh:uncategorized"));
    }

    // ── 5. Story directly under an epic → synthetic "Stories" feature ──

    [Fact]
    public async Task Import_StoryDirectlyUnderEpic_LandsInSyntheticStoriesFeature()
    {
        var repoId = await SeedRepoAsync();
        var gh = new StubGitHubClient()
            .IssuesFor("acme", "widgets", new[]
            {
                Issue(1, "Epic", "epic", subIssuesTotal: 1),
                Issue(4, "Direct story", "story")
            })
            .SubIssuesFor("acme", "widgets", 1, new[] { 4 });

        await using var ctx = NewContext();
        await NewImporter(ctx, gh).ImportAsync(repoId, "alice");

        var epic = await ctx.Epics.FirstAsync(e => e.ExternalId == "gh:1");
        var storiesFeature = await ctx.Features.FirstAsync(
            f => f.ExternalId == "gh:1/stories");
        Assert.Equal(epic.Id, storiesFeature.EpicId);
        Assert.Equal("Stories", storiesFeature.Title);

        var story = await ctx.UserStories.FirstAsync(s => s.ExternalId == "gh:4");
        Assert.Equal(storiesFeature.Id, story.FeatureId);

        // The direct story is categorized — no Uncategorized bucket.
        Assert.Null(await ctx.Epics.FirstOrDefaultAsync(
            e => e.ExternalId == "gh:uncategorized"));
        Assert.Null(await ctx.Features.FirstOrDefaultAsync(
            f => f.ExternalId == "gh:uncategorized"));
    }

    // ── 6. Re-sync re-homing + empty-bucket healing ─────────────────────

    [Fact]
    public async Task Import_Resync_WithSubIssues_RehomesRowsAndDeletesEmptyUncategorized()
    {
        var repoId = await SeedRepoAsync();

        // First sync: no hierarchy signal at all — feature and story
        // land under Uncategorized.
        var gh1 = new StubGitHubClient().IssuesFor("acme", "widgets", new[]
        {
            Issue(1, "Epic", "epic"),
            Issue(2, "Feature", "feature"),
            Issue(3, "Story", "story")
        });
        await using (var ctx1 = NewContext())
            await NewImporter(ctx1, gh1).ImportAsync(repoId, "alice");

        await using (var check = NewContext())
        {
            var uncatEpic = await check.Epics.FirstAsync(
                e => e.ExternalId == "gh:uncategorized");
            var uncatFeature = await check.Features.FirstAsync(
                f => f.ExternalId == "gh:uncategorized");
            var feature = await check.Features.FirstAsync(f => f.ExternalId == "gh:2");
            Assert.Equal(uncatEpic.Id, feature.EpicId);
            var story = await check.UserStories.FirstAsync(s => s.ExternalId == "gh:3");
            Assert.Equal(uncatFeature.Id, story.FeatureId);
        }

        // Second sync: the same issues now report native sub-issues.
        var gh2 = new StubGitHubClient()
            .IssuesFor("acme", "widgets", new[]
            {
                Issue(1, "Epic", "epic", subIssuesTotal: 1),
                Issue(2, "Feature", "feature", subIssuesTotal: 1),
                Issue(3, "Story", "story")
            })
            .SubIssuesFor("acme", "widgets", 1, new[] { 2 })
            .SubIssuesFor("acme", "widgets", 2, new[] { 3 });
        await using (var ctx2 = NewContext())
            await NewImporter(ctx2, gh2).ImportAsync(repoId, "alice");

        await using var ctx = NewContext();
        var epic = await ctx.Epics.FirstAsync(e => e.ExternalId == "gh:1");
        var rehomedFeature = await ctx.Features.FirstAsync(f => f.ExternalId == "gh:2");
        Assert.Equal(epic.Id, rehomedFeature.EpicId);
        var rehomedStory = await ctx.UserStories.FirstAsync(s => s.ExternalId == "gh:3");
        Assert.Equal(rehomedFeature.Id, rehomedStory.FeatureId);

        // The now-empty Uncategorized epic + feature are HEALED away.
        Assert.Null(await ctx.Epics.FirstOrDefaultAsync(
            e => e.ExternalId == "gh:uncategorized"));
        Assert.Null(await ctx.Features.FirstOrDefaultAsync(
            f => f.ExternalId == "gh:uncategorized"));
    }

    // ── 7. Orphans still fall back to Uncategorized ─────────────────────

    [Fact]
    public async Task Import_OrphansWithNoSignal_StillFallBackToUncategorized()
    {
        var repoId = await SeedRepoAsync();
        var gh = new StubGitHubClient().IssuesFor("acme", "widgets", new[]
        {
            Issue(2, "Orphan feature", "feature"),
            Issue(3, "Orphan story", "story")
        });

        await using var ctx = NewContext();
        await NewImporter(ctx, gh).ImportAsync(repoId, "alice");

        var uncatEpic = await ctx.Epics.FirstAsync(e => e.ExternalId == "gh:uncategorized");
        var uncatFeature = await ctx.Features.FirstAsync(f => f.ExternalId == "gh:uncategorized");
        Assert.Equal(uncatEpic.Id, uncatFeature.EpicId);

        var feature = await ctx.Features.FirstAsync(f => f.ExternalId == "gh:2");
        Assert.Equal(uncatEpic.Id, feature.EpicId);
        var story = await ctx.UserStories.FirstAsync(s => s.ExternalId == "gh:3");
        Assert.Equal(uncatFeature.Id, story.FeatureId);
    }

    // ── 8. ParseTaskListReferences semantics ────────────────────────────

    [Fact]
    public void ParseTaskListReferences_YieldsFirstRefPerLineOnly()
    {
        const string body = """
            Epic body.
            - [ ] #12 first feature, see also #13 and #14
            - [x] **SM.2** — Backend prerequisites (#1976)
            - [ ] no reference on this line
            Not a task: #99 (mention in prose)
            - not a checkbox #77
            * [ ] depends on #56
            """;
        var refs = BacklogGitHubImportService.ParseTaskListReferences(body).ToList();
        // One ref max per task-list line, the FIRST one; non-task-list
        // lines and ref-less lines yield nothing.
        Assert.Equal(new[] { 12, 1976, 56 }, refs);
    }

    [Fact]
    public void ParseTaskListReferences_FirstRefWins_AcrossHashAndUrlForms()
    {
        // URL appears before the bare ref on the line → URL wins.
        const string urlFirst =
            "- [ ] https://github.com/acme/widgets/issues/7 supersedes #8";
        Assert.Equal(new[] { 7 }, BacklogGitHubImportService
            .ParseTaskListReferences(urlFirst, "acme", "widgets").ToList());

        // Bare ref appears first → it wins over a later URL.
        const string hashFirst =
            "- [ ] #8 supersedes https://github.com/acme/widgets/issues/7";
        Assert.Equal(new[] { 8 }, BacklogGitHubImportService
            .ParseTaskListReferences(hashFirst, "acme", "widgets").ToList());
    }
}
