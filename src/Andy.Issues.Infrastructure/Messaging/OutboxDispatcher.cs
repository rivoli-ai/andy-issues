// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.Issues.Infrastructure.Messaging;

// Background worker that drains the OutboxEntry table to IMessageBus.
// One instance per service. Polls at a configurable interval, batches
// pending rows, publishes each to its target subject, records success
// or failure. Rows are never deleted — the outbox doubles as an audit
// log; a separate retention job may purge published rows older than
// N days.
//
// Retry semantics: on publish failure the row stays pending with
// AttemptCount incremented and LastError set. The dispatcher respects
// an exponential-backoff delay (OutboxDispatcherOptions.BackoffBase *
// 2^(AttemptCount-1), capped at BackoffMax) before reconsidering the
// row so a poison message does not spin the worker at full speed.
public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcher> _logger;
    private readonly OutboxDispatcherOptions _options;

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxDispatcher> logger,
        IOptions<OutboxDispatcherOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxDispatcher started (poll={Poll}, batch={Batch})",
            _options.PollInterval, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var drained = await DrainOnceAsync(stoppingToken);
                if (drained == 0)
                {
                    await Task.Delay(_options.PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxDispatcher tick failed; backing off");
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
        }

        _logger.LogInformation("OutboxDispatcher stopped");
    }

    // Drains one batch. Public-internal so integration tests can invoke
    // a single iteration deterministically instead of waiting on the
    // background loop's cadence.
    internal async Task<int> DrainOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var now = DateTimeOffset.UtcNow;

        // Select pending rows whose backoff window has elapsed. A fresh
        // row (AttemptCount == 0, LastAttemptAt == null) is always
        // eligible; a previously-failed row waits for the backoff delay.
        // The OrderBy keeps first-failed/first-retried semantics intact.
        var pending = await db.Set<OutboxEntry>()
            .Where(e => e.PublishedAt == null)
            .OrderBy(e => e.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return 0;
        }

        var eligible = pending.Where(e => IsEligible(e, now)).ToList();
        if (eligible.Count == 0)
        {
            return 0;
        }

        foreach (var entry in eligible)
        {
            try
            {
                var headers = new MessageHeaders(
                    MsgId: entry.Id,
                    CorrelationId: entry.CorrelationId,
                    CausationId: entry.CausationId,
                    Generation: entry.Generation);

                // Payload is stored as JSON text, but IMessageBus.PublishAsync
                // takes an object (it re-serializes). JsonDocument lets the
                // bus re-serialize verbatim without a typed round-trip.
                using var doc = JsonDocument.Parse(entry.PayloadJson);
                await bus.PublishAsync(entry.Subject, doc.RootElement, headers, ct);

                entry.PublishedAt = DateTimeOffset.UtcNow;
                entry.LastError = null;
            }
            catch (Exception ex)
            {
                entry.AttemptCount++;
                entry.LastAttemptAt = DateTimeOffset.UtcNow;
                entry.LastError = ex.Message;
                _logger.LogWarning(ex,
                    "Outbox entry {EntryId} publish failed (attempt {Attempt})",
                    entry.Id, entry.AttemptCount);
            }
        }

        await db.SaveChangesAsync(ct);
        return eligible.Count;
    }

    private bool IsEligible(OutboxEntry entry, DateTimeOffset now)
    {
        if (entry.AttemptCount == 0 || entry.LastAttemptAt is null)
        {
            return true;
        }

        var factor = Math.Pow(2, entry.AttemptCount - 1);
        var delayTicks = (long)(_options.BackoffBase.Ticks * factor);
        var delay = TimeSpan.FromTicks(Math.Min(delayTicks, _options.BackoffMax.Ticks));
        return now - entry.LastAttemptAt.Value >= delay;
    }
}
