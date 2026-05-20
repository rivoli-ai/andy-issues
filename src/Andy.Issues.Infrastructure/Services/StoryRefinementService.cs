// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Services;

// SP.0.4 (andy-issues#180 / conductor#1632) — orchestrator for the
// long-running story refinement flow. See IStoryRefinementService for
// the contract.
//
// Concurrency model:
//
//   • The controller scope handles validation + persists the
//     "started" state (bumps RefineVersion, sets RefinedAt to null
//     while in-flight stays implicit via the in-memory tracker), then
//     dispatches the agent call on a background task using a fresh
//     IServiceScope (so the request-scoped AppDbContext doesn't outlive
//     the response).
//
//   • The in-memory _inFlight tracker maps (storyId, agentId) → the
//     refineRunId + refineVersion of the currently-running invocation.
//     A second call within IdempotencyWindow returns the original run
//     reference without dispatching a new agent.
//
//   • On completion, the background task opens a fresh scope, reloads
//     the story, applies the agent output, appends an outbox row
//     (`andy.issues.events.story.{storyId}.triaged`), and saves.
//     Failures inside the agent leave the story in NotTriaged / the
//     previous Triaged state — the in-flight entry is cleared either
//     way so a retry can fire immediately rather than waiting out the
//     5-minute window.
//
// In-process idempotency is sufficient here because andy-issues runs
// as a single replica in the Conductor-embedded mode and behind a
// sticky load balancer in cloud mode (per ADR-0001 §4). A multi-replica
// rollout would replace this tracker with a Postgres row lock or a
// Redis SETNX — out of scope for SP.0.4.
public sealed class StoryRefinementService : IStoryRefinementService
{
    public static readonly TimeSpan IdempotencyWindow = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly IRepositoryAccessGuard _guard;
    private readonly IAgentsClient? _agents;
    private readonly IStoryTriageAgent _triageAgent;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StoryRefinementService>? _logger;
    private readonly IStoryRefinementClock _clock;
    private readonly IStoryRefinementTracker _tracker;

    public StoryRefinementService(
        AppDbContext db,
        IRepositoryAccessGuard guard,
        IStoryTriageAgent triageAgent,
        IServiceScopeFactory scopeFactory,
        IStoryRefinementTracker tracker,
        IAgentsClient? agents = null,
        IStoryRefinementClock? clock = null,
        ILogger<StoryRefinementService>? logger = null)
    {
        _db = db;
        _guard = guard;
        _triageAgent = triageAgent;
        _scopeFactory = scopeFactory;
        _tracker = tracker;
        _agents = agents;
        _clock = clock ?? new SystemRefinementClock();
        _logger = logger;
    }

    public async Task<StoryRefineResult> RefineAsync(
        Guid storyId,
        RefineStoryRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var story = await _db.UserStories
            .Include(s => s.Feature).ThenInclude(f => f.Epic)
            .FirstOrDefaultAsync(s => s.Id == storyId, ct);
        if (story is null) return StoryRefineResult.NotFound();
        if (!await _guard.CanViewAsync(story.Feature.Epic.RepositoryId, userId, ct))
            return StoryRefineResult.NotFound();

        // Resolve the agent. Spec: request.AgentId wins, else the
        // workspace default. If neither resolves we leave the story
        // untouched and surface AgentUnavailable so the caller can
        // configure Triage:AgentId / install a real agent.
        string? agentId = request.AgentId;
        if (string.IsNullOrWhiteSpace(agentId) && _agents is not null)
        {
            try
            {
                var descriptor = await _agents.GetTriageAgentAsync(ct);
                agentId = descriptor?.AgentId;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex,
                    "Triage agent lookup failed for story {StoryId}; falling back to default.",
                    storyId);
            }
        }

        if (string.IsNullOrWhiteSpace(agentId))
        {
            // Default to a deterministic identifier so the echo agent
            // produces consistent output even without an AgentsClient.
            agentId = "andy-default-triage";
        }

        var key = new RefineKey(storyId, agentId);

        // Idempotency: short-circuit if an in-flight run exists and is
        // still within the window. Expired entries are evicted on the
        // way past so they don't stick forever on a crash.
        if (_tracker.TryGet(key, out var existing))
        {
            if (_clock.UtcNow - existing.StartedAt < IdempotencyWindow)
            {
                return StoryRefineResult.Queued(
                    new StoryRefineRunDto(existing.RefineRunId, existing.RefineVersion));
            }
            _tracker.Remove(key);
        }

        var refineRunId = Guid.NewGuid();
        var targetVersion = story.RefineVersion + 1;
        var startedAt = _clock.UtcNow;
        _tracker.Set(key, new InFlightEntry(refineRunId, targetVersion, startedAt));

        // Capture the values we hand to the background task — never
        // close over `story` or `_db` because they're tied to the
        // request scope.
        var input = new StoryTriageAgentInput(
            StoryId: story.Id,
            Title: story.Title,
            Description: story.Description,
            AcceptanceCriteria: story.AcceptanceCriteria,
            Labels: story.Labels.ToList(),
            Instructions: request.Instructions,
            AgentId: agentId);

        var backgroundTask = Task.Run(() => ExecuteRefineAsync(input, refineRunId, targetVersion, userId, key));
        _tracker.TrackBackgroundTask(backgroundTask);

        return StoryRefineResult.Queued(new StoryRefineRunDto(refineRunId, targetVersion));
    }

    private async Task ExecuteRefineAsync(
        StoryTriageAgentInput input,
        Guid refineRunId,
        int targetVersion,
        string userId,
        RefineKey key)
    {
        try
        {
            StoryTriageAgentOutput output;
            try
            {
                output = await _triageAgent.RefineAsync(input, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Story triage agent {AgentId} failed for story {StoryId}",
                    input.AgentId, input.StoryId);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var story = await db.UserStories
                .Include(s => s.Feature).ThenInclude(f => f.Epic)
                .FirstOrDefaultAsync(s => s.Id == input.StoryId);
            if (story is null)
            {
                _logger?.LogWarning(
                    "Story {StoryId} vanished during refinement run {RunId}", input.StoryId, refineRunId);
                return;
            }

            // Apply the agent output. We re-read RefineVersion + 1
            // rather than trusting `targetVersion` so concurrent runs
            // (which the idempotency guard already rejects, but
            // defence-in-depth) don't desync the counter.
            story.RefineVersion = Math.Max(targetVersion, story.RefineVersion + 1);
            story.RefinedAt = _clock.UtcNow;
            story.RefinedBy = input.AgentId;
            story.RefinedDescription = output.RefinedDescription;
            story.AcceptanceCriteriaList = output.AcceptanceCriteria.ToList();
            story.Risks = output.Risks.ToList();
            story.TestPlan = output.TestPlan.ToList();
            story.Priority = output.Priority;
            story.Complexity = output.Complexity;
            story.Risk = output.Risk;
            story.SuggestedApproach = output.SuggestedApproach;

            // Capture the live ContentHash post-SaveChanges so the
            // snapshot reflects whatever the AppDbContext recomputes.
            // We force a recompute now (matches the hook) so the
            // snapshot matches the row that ships.
            story.ContentHash = Andy.Issues.Domain.Services.StoryContentHasher.Compute(story);
            story.StoryContentHashAtTriage = story.ContentHash;
            story.UpdatedAt = _clock.UtcNow;

            AppendTriageEvent(db, story);

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "Unhandled error completing refinement {RunId} for story {StoryId}",
                refineRunId, input.StoryId);
        }
        finally
        {
            _tracker.Remove(key);
        }
    }

    private static void AppendTriageEvent(AppDbContext db, UserStory story)
    {
        var classification = new StoryClassificationDto(
            Priority: StoryRefinementMapping.ToWire(story.Priority ?? StoryPriority.P2),
            Complexity: StoryRefinementMapping.ToWire(story.Complexity ?? StoryComplexity.Medium),
            Risk: StoryRefinementMapping.ToWire(story.Risk ?? StoryRisk.Medium),
            SuggestedApproach: story.SuggestedApproach);

        var triageState = story.DeriveTriageState(triaging: false);

        var payload = new StoryTriageCompletedEvent(
            StoryId: story.Id,
            FeatureId: story.FeatureId,
            EpicId: story.Feature.EpicId,
            RepositoryId: story.Feature.Epic.RepositoryId,
            DisplayId: story.DisplayId,
            RefineVersion: story.RefineVersion,
            RefinedAt: story.RefinedAt ?? DateTimeOffset.UtcNow,
            RefinedBy: story.RefinedBy ?? string.Empty,
            ContentHashAtTriage: story.StoryContentHashAtTriage,
            Classification: classification,
            TriageState: triageState);

        var subject = $"andy.issues.events.story.{story.Id}.triaged";
        db.Outbox.Add(new OutboxEntry
        {
            Id = Guid.NewGuid(),
            Subject = subject,
            PayloadType = typeof(StoryTriageCompletedEvent).FullName,
            PayloadJson = JsonSerializer.Serialize(payload, EventJson.Options),
            CorrelationId = story.Id,
            CausationId = null,
            Generation = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    // Testing seam — replaces DateTimeOffset.UtcNow so the idempotency
    // window expiration and RefinedAt timestamps are deterministic.
    public interface IStoryRefinementClock
    {
        DateTimeOffset UtcNow { get; }
    }

    private sealed class SystemRefinementClock : IStoryRefinementClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    public readonly record struct RefineKey(Guid StoryId, string AgentId);

    public readonly record struct InFlightEntry(
        Guid RefineRunId, int RefineVersion, DateTimeOffset StartedAt);
}

// Idempotency tracker abstraction so tests can construct an isolated
// instance instead of fighting a process-wide static. Production
// registers a single `InMemoryStoryRefinementTracker` as singleton in
// Program.cs; tests own one tracker per service-provider so background
// tasks across test classes can't race the same dictionary.
public interface IStoryRefinementTracker
{
    bool TryGet(StoryRefinementService.RefineKey key, out StoryRefinementService.InFlightEntry entry);
    void Set(StoryRefinementService.RefineKey key, StoryRefinementService.InFlightEntry entry);
    void Remove(StoryRefinementService.RefineKey key);
    void TrackBackgroundTask(Task task);
    Task DrainOutstandingTasksAsync(TimeSpan? timeout = null);
}

public sealed class InMemoryStoryRefinementTracker : IStoryRefinementTracker
{
    private readonly ConcurrentDictionary<StoryRefinementService.RefineKey, StoryRefinementService.InFlightEntry> _inFlight = new();
    private readonly ConcurrentBag<Task> _outstandingTasks = new();

    public bool TryGet(StoryRefinementService.RefineKey key, out StoryRefinementService.InFlightEntry entry) =>
        _inFlight.TryGetValue(key, out entry);

    public void Set(StoryRefinementService.RefineKey key, StoryRefinementService.InFlightEntry entry) =>
        _inFlight[key] = entry;

    public void Remove(StoryRefinementService.RefineKey key) => _inFlight.TryRemove(key, out _);

    public void TrackBackgroundTask(Task task) => _outstandingTasks.Add(task);

    public Task DrainOutstandingTasksAsync(TimeSpan? timeout = null)
    {
        var pending = new List<Task>();
        while (_outstandingTasks.TryTake(out var t))
        {
            if (!t.IsCompleted) pending.Add(t);
        }
        if (pending.Count == 0) return Task.CompletedTask;
        return Task.WhenAny(
            Task.WhenAll(pending),
            Task.Delay(timeout ?? TimeSpan.FromSeconds(5)));
    }
}
