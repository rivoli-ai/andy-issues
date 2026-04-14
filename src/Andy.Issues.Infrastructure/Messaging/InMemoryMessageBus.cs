// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Andy.Issues.Application.Messaging;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Messaging;

// In-process IMessageBus implementation. Used in tests and as the default
// development provider until NatsMessageBus lands (Story 15.7). Supports
// NATS subject wildcards: '*' matches one token, '>' matches one or more
// trailing tokens.
//
// Not a drop-in production replacement for NATS: no durability (messages
// vanish on restart), no cross-process delivery, no back-pressure beyond
// channel capacity. But it faithfully implements the IMessageBus contract
// so the outbox + dispatcher + bus wiring can be proved end-to-end before
// the real NATS client arrives.
//
// Runtime cycle breaker (ADR 0001 §5): PublishAsync drops messages whose
// Headers.ExceedsGenerationLimit is true, logs at Error with the full
// causation chain, and returns. The drop happens before any subscriber
// is notified.
public sealed class InMemoryMessageBus : IMessageBus
{
    private readonly ILogger<InMemoryMessageBus> _logger;

    // One unbounded channel per active subscription, keyed by the exact
    // subject filter the subscriber passed. Two subscribers on different
    // filters get their own channels; fan-out happens in PublishAsync.
    private readonly ConcurrentDictionary<string, Channel<IncomingMessage>> _subscriptions = new();

    public InMemoryMessageBus(ILogger<InMemoryMessageBus> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(
        string subject,
        object payload,
        MessageHeaders headers,
        CancellationToken ct = default)
    {
        if (headers.ExceedsGenerationLimit)
        {
            _logger.LogError(
                "Dropping message {MsgId} on {Subject} — generation {Gen} exceeds limit {Max}. " +
                "Correlation: {CorrId} Causation: {CausedBy}",
                headers.MsgId, subject, headers.Generation, MessageHeaders.MaxGeneration,
                headers.CorrelationId, headers.CausationId);
            return Task.CompletedTask;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, EventJson.Options);

        foreach (var (filter, channel) in _subscriptions)
        {
            if (!MatchesSubject(filter, subject))
            {
                continue;
            }

            var message = new InMemoryIncomingMessage
            {
                Headers = headers,
                Subject = subject,
                Payload = bytes,
                ReceivedAt = DateTimeOffset.UtcNow,
            };

            channel.Writer.TryWrite(message);
        }

        return Task.CompletedTask;
    }

    // Pre-register the channel for a subject filter. Exposed so tests can
    // avoid the race between "start subscriber task" and "publish first
    // message" — calling this before publishing guarantees the channel
    // exists. Production callers don't need it; SubscribeAsync uses the
    // same GetOrAdd internally.
    internal Channel<IncomingMessage> EnsureChannel(string subjectFilter)
    {
        return _subscriptions.GetOrAdd(
            subjectFilter,
            _ => Channel.CreateUnbounded<IncomingMessage>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false,
            }));
    }

    public async IAsyncEnumerable<IncomingMessage> SubscribeAsync(
        string subjectFilter,
        SubscriptionOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = EnsureChannel(subjectFilter);

        _logger.LogDebug("Subscription opened on {Filter} durable {Durable}",
            subjectFilter, options.DurableName);

        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(ct))
            {
                yield return message;
            }
        }
        finally
        {
            _subscriptions.TryRemove(subjectFilter, out _);
            _logger.LogDebug("Subscription closed on {Filter}", subjectFilter);
        }
    }

    // NATS subject match semantics:
    //   '*' matches exactly one token
    //   '>' matches one or more trailing tokens (must be the last token)
    internal static bool MatchesSubject(string filter, string subject)
    {
        if (filter == subject)
        {
            return true;
        }

        var filterTokens = filter.Split('.');
        var subjectTokens = subject.Split('.');

        for (int i = 0; i < filterTokens.Length; i++)
        {
            if (filterTokens[i] == ">")
            {
                return i == filterTokens.Length - 1 && i < subjectTokens.Length;
            }

            if (i >= subjectTokens.Length)
            {
                return false;
            }

            if (filterTokens[i] != "*" && filterTokens[i] != subjectTokens[i])
            {
                return false;
            }
        }

        return filterTokens.Length == subjectTokens.Length;
    }
}

internal sealed class InMemoryIncomingMessage : IncomingMessage
{
    public override Task AckAsync(CancellationToken ct = default) => Task.CompletedTask;
    public override Task NackAsync(CancellationToken ct = default) => Task.CompletedTask;
}
