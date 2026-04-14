// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;

namespace Andy.Issues.Application.Messaging;

// Cross-service messaging abstraction. Implementations publish to a bus
// (default: NATS JetStream per ADR 0001; see docs/adr/0001-messaging.md)
// and yield incoming messages from durable subscriptions.
//
// The abstraction is intentionally small. PublishAsync serializes the
// payload to JSON bytes; SubscribeAsync yields IncomingMessage instances
// whose callers decode via Deserialize<T>. Subject taxonomy and causation
// rules are enforced by callers, not by the bus — the bus is transport.
public interface IMessageBus
{
    // Publish a single message. Callers should not call this directly in
    // most paths; prefer writing an OutboxEntry in the same transaction as
    // the domain change and letting the OutboxDispatcher drain it. Direct
    // publish is appropriate only for ephemeral messages that do not need
    // at-least-once delivery (e.g. system.health heartbeats).
    Task PublishAsync(
        string subject,
        object payload,
        MessageHeaders headers,
        CancellationToken ct = default);

    // Create a durable subscription. The stream yields incoming messages
    // until cancellation. Consumers are expected to call AckAsync or
    // NackAsync on each IncomingMessage. Generation-limit overflow is
    // handled by the bus: offending messages are dropped before reaching
    // the consumer, with an error-level log that includes the causation
    // chain.
    IAsyncEnumerable<IncomingMessage> SubscribeAsync(
        string subjectFilter,
        SubscriptionOptions options,
        CancellationToken ct = default);
}

// Four-field header envelope required on every message per ADR 0001 §5.
public sealed record MessageHeaders(
    Guid MsgId,
    Guid CorrelationId,
    Guid? CausationId,
    int Generation)
{
    // Hop limit from ADR 0001. Messages exceeding this generation count
    // are dropped by the bus as a runtime circuit breaker for cycles.
    public const int MaxGeneration = 10;

    // Start a new causation chain. Use for messages triggered directly by
    // user input or by a scheduled worker tick — anything not produced
    // in response to an incoming message.
    public static MessageHeaders NewRoot(Guid? correlationId = null)
    {
        var id = Guid.NewGuid();
        return new MessageHeaders(
            MsgId: id,
            CorrelationId: correlationId ?? id,
            CausationId: null,
            Generation: 0);
    }

    // Continue an existing causation chain when emitting a message in
    // response to an incoming one.
    public static MessageHeaders Follow(MessageHeaders parent) => new(
        MsgId: Guid.NewGuid(),
        CorrelationId: parent.CorrelationId,
        CausationId: parent.MsgId,
        Generation: parent.Generation + 1);

    public bool ExceedsGenerationLimit => Generation > MaxGeneration;
}

// Abstract incoming-message wrapper. Concrete implementations attach the
// provider-specific ack/nack mechanism (JetStream consumer in production,
// no-op for the in-memory bus).
public abstract class IncomingMessage
{
    public required MessageHeaders Headers { get; init; }
    public required string Subject { get; init; }
    public required ReadOnlyMemory<byte> Payload { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }

    // Decode the payload as the given type. Returns null if the payload
    // is empty. Callers pick the expected type from their subscription's
    // subject filter. Defaults to the canonical ADR 0001 wire options
    // (EventJson.Options) so producers and consumers don't negotiate
    // snake_case/camelCase out of band.
    public T? Deserialize<T>(JsonSerializerOptions? options = null) where T : class
    {
        if (Payload.IsEmpty) return null;
        return JsonSerializer.Deserialize<T>(Payload.Span, options ?? EventJson.Options);
    }

    public abstract Task AckAsync(CancellationToken ct = default);
    public abstract Task NackAsync(CancellationToken ct = default);
}

public sealed record SubscriptionOptions(
    // Durable consumer name. Required for JetStream so offsets survive
    // restarts. Must be stable across restarts — typically
    // "andy-issues.<purpose>" (e.g. "andy-issues.run-events").
    string DurableName,
    // Maximum delivery attempts before moving to the dead-letter subject.
    int MaxDeliver = 10,
    // Whether the consumer must explicitly Ack/Nack each message. When
    // false, the bus auto-acks on delivery (at-most-once).
    bool ManualAck = true,
    // Optional narrower filter within the base subject filter. Useful
    // for splitting one subscription into multiple handlers without
    // creating multiple durable consumers.
    string? SubjectFilter = null);
