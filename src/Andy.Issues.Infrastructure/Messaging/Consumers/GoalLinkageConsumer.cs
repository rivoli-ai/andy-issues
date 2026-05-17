// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Messaging.Consumers;

/// <summary>
/// AH6 (rivoli-ai/conductor#713) — subscribes to
/// <c>andy.tasks.events.goal.&gt;</c> and writes the reverse pin
/// <see cref="Andy.Issues.Domain.Entities.Issue.GoalDisplayId"/> onto
/// the originating Issue when the payload carries both
/// <c>GoalDisplayId</c> and <c>SourceIssueDisplayId</c> in the
/// <c>ISSUE-N</c> form.
/// </summary>
/// <remarks>
/// The linkage write is best-effort by design:
/// - No match for <c>SourceIssueDisplayId</c> → log + ack. The goal
///   may have been created against an Issue we never knew about
///   (cross-tenant edge cases, manual goal creation that fakes a
///   source id) — we don't want at-least-once redelivery to thrash.
/// - <c>SourceIssueDisplayId</c> null → no linkage to write. This is
///   the normal case for human-created goals on the andy-tasks side.
/// - Wrong shape (anything that doesn't parse as <c>ISSUE-N</c>) →
///   log + ack. We only own the ISSUE prefix; STORY/EPIC/FEAT
///   sources go to a different (currently nonexistent) subscriber
///   surface and are silently ignored.
///
/// Idempotency: re-applying the same linkage write is benign — the
/// column already contains the goal display id from the previous
/// delivery, so the second write is a no-op (we skip the
/// SaveChanges entirely). An in-memory ring buffer of recent msg-ids
/// catches the rare case where processing succeeded but the ack
/// lost the race.
/// </remarks>
public sealed class GoalLinkageConsumer : BackgroundService
{
    private const string SubjectFilter = "andy.tasks.events.goal.>";
    private const string DurableName = "andy-issues-goal-linkage";
    private const int DedupeBufferSize = 1024;
    private const string IssuePrefix = "ISSUE-";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageBus _bus;
    private readonly ILogger<GoalLinkageConsumer> _logger;

    private readonly ConcurrentQueue<Guid> _recentMsgIdsOrder = new();
    private readonly ConcurrentDictionary<Guid, byte> _recentMsgIds = new();

    public GoalLinkageConsumer(
        IServiceScopeFactory scopeFactory,
        IMessageBus bus,
        ILogger<GoalLinkageConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "GoalLinkageConsumer subscribing on {Filter} durable {Durable}",
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
                _logger.LogWarning(ex,
                    "GoalLinkageConsumer subscription loop failed (likely upstream " +
                    "ANDY_TASKS JetStream not provisioned yet); retrying in 5s");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("GoalLinkageConsumer stopped");
    }

    internal async Task HandleAsync(IncomingMessage msg, CancellationToken ct)
    {
        if (!TryRemember(msg.Headers.MsgId))
        {
            _logger.LogDebug(
                "GoalLinkageConsumer: skipping duplicate msg-id {MsgId}",
                msg.Headers.MsgId);
            await msg.AckAsync(ct);
            return;
        }

        // We only care about goal.created — andy-tasks emits other
        // subjects on the same prefix (goal.completed, etc.) that we
        // don't need for the linkage write.
        if (!msg.Subject.EndsWith(".created", StringComparison.Ordinal))
        {
            await msg.AckAsync(ct);
            return;
        }

        GoalCreatedEventPayload? payload;
        try
        {
            payload = msg.Deserialize<GoalCreatedEventPayload>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "GoalLinkageConsumer: failed to deserialize payload on {Subject} — acking",
                msg.Subject);
            await msg.AckAsync(ct);
            return;
        }

        if (payload is null)
        {
            await msg.AckAsync(ct);
            return;
        }

        // Skip rows that don't carry the linkage — human-created
        // goals on the andy-tasks side, or pre-AH6 emissions that
        // predate the SourceIssueDisplayId field.
        if (string.IsNullOrWhiteSpace(payload.SourceIssueDisplayId)
            || string.IsNullOrWhiteSpace(payload.GoalDisplayId))
        {
            await msg.AckAsync(ct);
            return;
        }

        // We only own the ISSUE-N prefix. STORY/EPIC/FEAT sources are
        // out of scope here (they belong to backlog-entity linkage,
        // not the Issue triage envelope).
        if (!payload.SourceIssueDisplayId.StartsWith(IssuePrefix, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "GoalLinkageConsumer: ignoring non-ISSUE source {Source} on {Subject}",
                payload.SourceIssueDisplayId, msg.Subject);
            await msg.AckAsync(ct);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // DisplayId is a computed property (not mapped) so EF can't
        // translate `i.DisplayId == "ISSUE-N"` to SQL. Parse the Seq
        // off the wire form and look up the indexed column instead.
        var issue = await FindBySeqAsync(db, payload.SourceIssueDisplayId, ct);

        if (issue is null)
        {
            _logger.LogInformation(
                "GoalLinkageConsumer: no Issue matched source {Source} on {Subject} — " +
                "may have been deleted or never existed locally",
                payload.SourceIssueDisplayId, msg.Subject);
            await msg.AckAsync(ct);
            return;
        }

        // Idempotency: re-delivery of the same goal.created shouldn't
        // re-write the link. The column is immutable by contract;
        // skip the SaveChanges entirely.
        if (issue.GoalDisplayId == payload.GoalDisplayId)
        {
            await msg.AckAsync(ct);
            return;
        }

        if (!string.IsNullOrEmpty(issue.GoalDisplayId)
            && issue.GoalDisplayId != payload.GoalDisplayId)
        {
            // Already linked to a *different* goal. Don't overwrite —
            // log so an operator can investigate, then ack so we don't
            // thrash on redelivery.
            _logger.LogWarning(
                "GoalLinkageConsumer: {Source} already linked to {Existing}, " +
                "rejecting new link to {New} on {Subject}",
                payload.SourceIssueDisplayId, issue.GoalDisplayId,
                payload.GoalDisplayId, msg.Subject);
            await msg.AckAsync(ct);
            return;
        }

        issue.GoalDisplayId = payload.GoalDisplayId;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "GoalLinkageConsumer: pinned {Source} → {Goal} on {Subject}",
            payload.SourceIssueDisplayId, payload.GoalDisplayId, msg.Subject);

        await msg.AckAsync(ct);
    }

    // Parses ISSUE-{n} and looks up by the Seq column directly —
    // EF can translate Seq comparisons but not the computed DisplayId
    // property.
    private static async Task<Andy.Issues.Domain.Entities.Issue?> FindBySeqAsync(
        AppDbContext db, string displayId, CancellationToken ct)
    {
        if (!displayId.StartsWith(IssuePrefix, StringComparison.Ordinal))
        {
            return null;
        }
        var suffix = displayId.Substring(IssuePrefix.Length);
        if (!long.TryParse(suffix, out long seq) || seq <= 0)
        {
            return null;
        }
        return await db.Issues.FirstOrDefaultAsync(i => i.Seq == seq, ct);
    }

    // Mirrors the bounded ring buffer in ContainerRunEventConsumer —
    // catches the rare case where processing succeeded but the ack
    // lost the race.
    private bool TryRemember(Guid msgId)
    {
        if (!_recentMsgIds.TryAdd(msgId, 0))
        {
            return false;
        }
        _recentMsgIdsOrder.Enqueue(msgId);
        while (_recentMsgIdsOrder.Count > DedupeBufferSize
            && _recentMsgIdsOrder.TryDequeue(out var evicted))
        {
            _recentMsgIds.TryRemove(evicted, out _);
        }
        return true;
    }
}
