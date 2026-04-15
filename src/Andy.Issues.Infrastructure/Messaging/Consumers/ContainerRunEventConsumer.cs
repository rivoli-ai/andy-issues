// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Messaging.Consumers;

// Subscribes to andy.containers.events.run.> and correlates terminal
// run events back to UserStory state per Story 15.6.
//
// Gated on Messaging:ConsumeRunEvents=true so this stays off until the
// publisher side in andy-containers is rolled out. When disabled the
// service registers but ExecuteAsync returns immediately — zero
// runtime cost.
//
// Idempotency: in-memory bounded ring buffer of the most recently seen
// msg-ids. NATS JetStream's at-least-once redelivery is the primary
// duplicate source; the buffer catches the rare case where processing
// succeeds but the ack loses the race. UserStory.SetStatus is itself
// idempotent (setting a state it's already in is a no-op per SetStatus
// semantics), so at worst a true duplicate writes an extra log line.
public sealed class ContainerRunEventConsumer : BackgroundService
{
    // Matches all three terminal kinds — finished, failed, cancelled —
    // that andy-containers publishes (see its run event publisher).
    private const string SubjectFilter = "andy.containers.events.run.>";
    private const string DurableName = "andy-issues-run-events";
    private const int DedupeBufferSize = 1024;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageBus _bus;
    private readonly ILogger<ContainerRunEventConsumer> _logger;
    private readonly bool _enabled;

    private readonly ConcurrentQueue<Guid> _recentMsgIdsOrder = new();
    private readonly ConcurrentDictionary<Guid, byte> _recentMsgIds = new();

    public ContainerRunEventConsumer(
        IServiceScopeFactory scopeFactory,
        IMessageBus bus,
        IConfiguration configuration,
        ILogger<ContainerRunEventConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _bus = bus;
        _logger = logger;
        _enabled = configuration.GetValue<bool>("Messaging:ConsumeRunEvents");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation(
                "ContainerRunEventConsumer disabled " +
                "(Messaging:ConsumeRunEvents is not true).");
            return;
        }

        _logger.LogInformation(
            "ContainerRunEventConsumer subscribing on {Filter} durable {Durable}",
            SubjectFilter, DurableName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var msg in _bus.SubscribeAsync(
                    SubjectFilter,
                    new SubscriptionOptions(DurableName),
                    stoppingToken))
                {
                    await HandleAsync(msg, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ContainerRunEventConsumer subscription loop failed; restarting in 5s");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("ContainerRunEventConsumer stopped");
    }

    internal async Task HandleAsync(IncomingMessage msg, CancellationToken ct)
    {
        if (!TryRemember(msg.Headers.MsgId))
        {
            _logger.LogDebug("Skipping duplicate msg-id {MsgId}", msg.Headers.MsgId);
            await msg.AckAsync(ct);
            return;
        }

        ContainerRunEventPayload? payload;
        try
        {
            payload = msg.Deserialize<ContainerRunEventPayload>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to deserialize run event on {Subject}; acking to avoid redelivery",
                msg.Subject);
            await msg.AckAsync(ct);
            return;
        }

        if (payload?.StoryId is null)
        {
            // Run was not correlated to a backlog story — nothing to do.
            await msg.AckAsync(ct);
            return;
        }

        var kind = ParseKind(msg.Subject);
        if (kind is null)
        {
            _logger.LogWarning(
                "Unknown run event kind on {Subject}; acking without action",
                msg.Subject);
            await msg.AckAsync(ct);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var story = await db.UserStories.FirstOrDefaultAsync(s => s.Id == payload.StoryId.Value, ct);
        if (story is null)
        {
            // Story may have been deleted between run creation and event
            // delivery. Ack and move on — no recovery to attempt.
            _logger.LogDebug("No story found for run event {MsgId} story_id={StoryId}",
                msg.Headers.MsgId, payload.StoryId);
            await msg.AckAsync(ct);
            return;
        }

        switch (kind)
        {
            case "finished":
                // Transition to InReview. SetStatus is idempotent if the
                // story is already InReview; Done → Draft is the only
                // rejected transition and is not reachable here.
                if (story.Status != UserStoryStatus.InReview && story.Status != UserStoryStatus.Done)
                {
                    story.SetStatus(UserStoryStatus.InReview);
                    await db.SaveChangesAsync(ct);
                    _logger.LogInformation(
                        "Story {StoryId} → InReview after run {RunId} finished",
                        story.Id, payload.RunId);
                }
                break;

            case "failed":
            case "cancelled":
                // Leave state alone; surface for operator inspection. An
                // activity-log entity is tracked as a future enhancement.
                _logger.LogInformation(
                    "Run {RunId} {Kind} for story {StoryId} (status unchanged: {Status})",
                    payload.RunId, kind, story.Id, story.Status);
                break;
        }

        await msg.AckAsync(ct);
    }

    // Returns true if the id was NOT previously seen (and remembers it
    // now); false if it was already in the buffer.
    private bool TryRemember(Guid msgId)
    {
        if (!_recentMsgIds.TryAdd(msgId, 0))
            return false;

        _recentMsgIdsOrder.Enqueue(msgId);
        while (_recentMsgIdsOrder.Count > DedupeBufferSize
               && _recentMsgIdsOrder.TryDequeue(out var evicted))
        {
            _recentMsgIds.TryRemove(evicted, out _);
        }
        return true;
    }

    private static string? ParseKind(string subject)
    {
        // Expected shape: andy.containers.events.run.<guid>.<kind>
        var lastDot = subject.LastIndexOf('.');
        if (lastDot < 0 || lastDot == subject.Length - 1) return null;
        var kind = subject[(lastDot + 1)..];
        return kind is "finished" or "failed" or "cancelled" ? kind : null;
    }
}
