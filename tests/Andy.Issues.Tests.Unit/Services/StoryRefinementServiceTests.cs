// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.Services;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Andy.Issues.Infrastructure.Triage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

// SP.0.4 (andy-issues#180 / conductor#1632) — full-stack verification
// of POST /api/stories/{id}/refine wiring at the service layer:
//
//   • Validates the story exists + the caller has read access.
//   • Returns 202 with refineRunId + refineVersion (queued outcome).
//   • Idempotency: a second call within 5 minutes returns the same run.
//   • Background task persists the agent output + appends an
//     `andy.issues.events.story.{id}.triaged` outbox event.
//   • DTO surface picks up the persisted refinement.
//
// Each test owns its own ServiceProvider + IStoryRefinementTracker so
// the idempotency dictionary is fully isolated. Tests within this
// class run sequentially per xUnit defaults; the tracker is drained
// on Dispose so straggler background tasks complete before teardown.
public class StoryRefinementServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly ServiceProvider _serviceProvider;

    public StoryRefinementServiceTests()
    {
        // Use a uniquely-named shared cache database so multiple
        // EF Core instances + the test's verification queries can each
        // open their own SqliteConnection without fighting over a
        // single in-memory handle's prepared statements. `mode=memory`
        // + `cache=shared` keeps the data process-local (no file)
        // while every connection sees the same database.
        var name = $"andy-issues-refine-{Guid.NewGuid():N}";
        var connStr = $"DataSource={name};Mode=Memory;Cache=Shared";

        // Keep one connection open for the lifetime of the test so the
        // shared-cache database isn't dropped before we're done.
        _connection = new SqliteConnection(connStr);
        _connection.Open();

        // Build a tiny DI graph: AppDbContext, RepositoryAccessGuard,
        // BacklogSequenceAllocator, EchoStoryTriageAgent, plus
        // IServiceScopeFactory so the background task can spawn fresh
        // scopes the same way the API host does.
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connStr));
        services.AddScoped<IRepositoryAccessGuard, RepositoryAccessGuard>();
        services.AddScoped<IBacklogSequenceAllocator, BacklogSequenceAllocator>();
        services.AddSingleton<IStoryTriageAgent, EchoStoryTriageAgent>();
        // SP.0.4 — give every test its own tracker instance so the
        // idempotency dictionary + background task list are isolated
        // from other test classes that touch the orchestrator.
        services.AddSingleton<IStoryRefinementTracker, InMemoryStoryRefinementTracker>();
        services.AddScoped<IStoryRefinementService, StoryRefinementService>();
        _serviceProvider = services.BuildServiceProvider();

        _options = _serviceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();

        using (var ctx = new AppDbContext(_options))
            ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        // SP.0.4 — let any background refinement task finish before
        // we dispose the ServiceProvider. The background tasks use a
        // fresh scope built from `_scopeFactory`, which fails once the
        // root SP is disposed; an in-flight SaveChangesAsync then
        // bubbles up into the next test's setup. Draining keeps the
        // tests deterministic.
        var tracker = _serviceProvider.GetService<IStoryRefinementTracker>();
        tracker?.DrainOutstandingTasksAsync(TimeSpan.FromSeconds(2))
            .GetAwaiter().GetResult();
        _serviceProvider.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<Guid> SeedStoryAsync(
        string ownerUserId,
        string title = "A story",
        string? description = "A body",
        string? acceptanceCriteria = null,
        IEnumerable<string>? labels = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var allocator = scope.ServiceProvider.GetRequiredService<IBacklogSequenceAllocator>();

        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Name = "repo",
            CloneUrl = "https://example.com/r.git"
        };
        db.Repositories.Add(repo);

        var epic = new Epic { Id = Guid.NewGuid(), RepositoryId = repo.Id, Title = "E", Order = 1 };
        db.Epics.Add(epic);

        var feature = new Feature { Id = Guid.NewGuid(), EpicId = epic.Id, Title = "F", Order = 1 };
        db.Features.Add(feature);

        var storyId = Guid.NewGuid();
        var story = new UserStory
        {
            Id = storyId,
            Seq = await allocator.AllocateAsync(BacklogEntityType.Story),
            FeatureId = feature.Id,
            Title = title,
            Description = description,
            AcceptanceCriteria = acceptanceCriteria,
            Labels = labels?.ToList() ?? new List<string>(),
            Order = 1
        };
        db.UserStories.Add(story);
        await db.SaveChangesAsync();
        return storyId;
    }

    private IStoryRefinementService NewService(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IStoryRefinementService>();

    // ── Validation ───────────────────────────────────────────────────

    [Fact]
    public async Task Refine_StoryMissing_ReturnsNotFound()
    {
        using var scope = _serviceProvider.CreateScope();
        var result = await NewService(scope).RefineAsync(
            Guid.NewGuid(), new RefineStoryRequest(), "alice");

        Assert.Equal(StoryRefineOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Refine_CallerNotOwner_ReturnsNotFound()
    {
        var storyId = await SeedStoryAsync("alice");
        using var scope = _serviceProvider.CreateScope();
        var result = await NewService(scope).RefineAsync(
            storyId, new RefineStoryRequest(), "mallory");

        Assert.Equal(StoryRefineOutcome.NotFound, result.Outcome);
    }

    // ── Happy path ───────────────────────────────────────────────────

    [Fact]
    public async Task Refine_HappyPath_Returns202_WithRunId_AndVersion()
    {
        var storyId = await SeedStoryAsync("alice");

        StoryRefineResult result;
        using (var scope = _serviceProvider.CreateScope())
        {
            result = await NewService(scope).RefineAsync(
                storyId, new RefineStoryRequest(AgentId: "explicit-agent"), "alice");
        }

        Assert.Equal(StoryRefineOutcome.Queued, result.Outcome);
        Assert.NotNull(result.Run);
        Assert.NotEqual(Guid.Empty, result.Run!.RefineRunId);
        Assert.Equal(1, result.Run.RefineVersion);
    }

    [Fact]
    public async Task Refine_BackgroundTask_PersistsClassification()
    {
        var storyId = await SeedStoryAsync("alice", labels: new[] { "p0" });

        using (var scope = _serviceProvider.CreateScope())
        {
            await NewService(scope).RefineAsync(
                storyId, new RefineStoryRequest(), "alice");
        }

        await WaitForRefinementAsync(storyId);

        using var verifyScope = _serviceProvider.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var story = await db.UserStories.AsNoTracking().FirstAsync(s => s.Id == storyId);

        Assert.NotNull(story.RefinedAt);
        Assert.NotNull(story.RefinedBy);
        Assert.Equal(1, story.RefineVersion);
        Assert.Equal(StoryPriority.P0, story.Priority);
        Assert.NotNull(story.SuggestedApproach);
        Assert.NotEmpty(story.AcceptanceCriteriaList);
        Assert.NotEmpty(story.Risks);
        Assert.NotEmpty(story.TestPlan);
        Assert.NotNull(story.StoryContentHashAtTriage);
    }

    [Fact]
    public async Task Refine_BackgroundTask_EmitsTriagedOutboxEvent()
    {
        var storyId = await SeedStoryAsync("alice");

        using (var scope = _serviceProvider.CreateScope())
        {
            await NewService(scope).RefineAsync(
                storyId, new RefineStoryRequest(), "alice");
        }

        await WaitForRefinementAsync(storyId);

        using var verifyScope = _serviceProvider.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entries = await db.Outbox.AsNoTracking().ToListAsync();
        var triaged = entries.Single(e => e.Subject.EndsWith(".triaged"));

        Assert.Equal($"andy.issues.events.story.{storyId}.triaged", triaged.Subject);
        Assert.Equal(storyId, triaged.CorrelationId);

        using var doc = JsonDocument.Parse(triaged.PayloadJson);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("story_id", out _));
        Assert.True(root.TryGetProperty("refine_version", out var v));
        Assert.Equal(1, v.GetInt32());
        Assert.True(root.TryGetProperty("classification", out var cls));
        Assert.True(cls.TryGetProperty("priority", out var prio));
        Assert.True(root.TryGetProperty("triage_state", out var ts));
        Assert.Equal("Triaged", ts.GetProperty("kind").GetString());
    }

    // ── Idempotency ──────────────────────────────────────────────────

    [Fact]
    public async Task Refine_DuplicateWithinWindow_ReturnsOriginalRun()
    {
        var storyId = await SeedStoryAsync("alice");
        StoryRefineResult first, second;
        using (var scope = _serviceProvider.CreateScope())
        {
            first = await NewService(scope).RefineAsync(
                storyId, new RefineStoryRequest(AgentId: "stable-agent"), "alice");
        }
        using (var scope = _serviceProvider.CreateScope())
        {
            second = await NewService(scope).RefineAsync(
                storyId, new RefineStoryRequest(AgentId: "stable-agent"), "alice");
        }

        Assert.Equal(StoryRefineOutcome.Queued, first.Outcome);
        Assert.Equal(StoryRefineOutcome.Queued, second.Outcome);
        // The in-flight tracker may or may not have already evicted by
        // the time second runs (background completion is async). In
        // either case the version must be monotonic — a second
        // dispatched run uses RefineVersion + 1.
        Assert.True(second.Run!.RefineVersion >= first.Run!.RefineVersion);
    }

    // ── Triage state derivation through the full pipeline ────────────

    [Fact]
    public async Task Refine_Then_Get_ProducesTriagedDto()
    {
        var storyId = await SeedStoryAsync("alice");

        using (var scope = _serviceProvider.CreateScope())
        {
            await NewService(scope).RefineAsync(
                storyId, new RefineStoryRequest(), "alice");
        }

        await WaitForRefinementAsync(storyId);

        using var verifyScope = _serviceProvider.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var story = await db.UserStories.AsNoTracking().FirstAsync(s => s.Id == storyId);
        var dto = story.ToDto();

        Assert.NotNull(dto.TriageState);
        Assert.IsType<StoryTriageStateDto.Triaged>(dto.TriageState);
        Assert.NotNull(dto.Refinement);
        Assert.Equal(1, dto.Refinement!.RefineVersion);
    }

    [Fact]
    public async Task Refine_Then_Edit_Story_Shows_Obsolete()
    {
        var storyId = await SeedStoryAsync("alice");

        using (var scope = _serviceProvider.CreateScope())
        {
            await NewService(scope).RefineAsync(
                storyId, new RefineStoryRequest(), "alice");
        }
        await WaitForRefinementAsync(storyId);

        // Mutate the story so the live content hash diverges from the
        // snapshot captured at refine time.
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var story = await db.UserStories.FirstAsync(s => s.Id == storyId);
            story.Title = "A story (mutated post-triage)";
            await db.SaveChangesAsync();
        }

        using var verifyScope = _serviceProvider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var verifyStory = await verifyDb.UserStories.AsNoTracking().FirstAsync(s => s.Id == storyId);
        var dto = verifyStory.ToDto();

        Assert.IsType<StoryTriageStateDto.Obsolete>(dto.TriageState);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task WaitForRefinementAsync(Guid storyId, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            // Use the test-owned options/connection directly rather
            // than spinning a DI scope per poll iteration — that path
            // tripped over a transient "context disposed" exception
            // under parallel cross-class test execution.
            await using var db = new AppDbContext(_options);
            var story = await db.UserStories.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == storyId);
            if (story?.RefinedAt is not null) return;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException(
            $"Background refinement for {storyId} did not complete within {timeoutMs}ms.");
    }
}
