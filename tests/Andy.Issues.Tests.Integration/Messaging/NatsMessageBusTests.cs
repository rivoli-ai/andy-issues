// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Infrastructure.Messaging.Nats;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.JetStream.Models;
using Xunit;

namespace Andy.Issues.Tests.Integration.Messaging;

// End-to-end tests against a real NATS JetStream server. Each test
// uses its own stream name + subject prefix so parallel / sequential
// runs cannot hit "subjects overlap" in JetStream and stale state from
// previous test runs cannot leak in.
//
// Run locally:
//   docker compose up -d nats
//   ANDY_ISSUES_TEST_NATS=true dotnet test tests/Andy.Issues.Tests.Integration
public class NatsMessageBusTests
{
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);

    private static string NatsUrl =>
        Environment.GetEnvironmentVariable("NATS_URL")
        ?? Environment.GetEnvironmentVariable("Messaging__Nats__Url")
        ?? "nats://localhost:4222";

    private static async Task<(NatsMessageBus Bus, string SubjectPrefix, NatsOptions Opts)> CreateAndConnectAsync()
    {
        var testId = Guid.NewGuid().ToString("N");
        var subjectPrefix = $"andy.test-{testId}";

        var opts = new NatsOptions
        {
            Url = NatsUrl,
            StreamName = $"ANDY_TEST_{testId}",
            StreamSubjects = [$"{subjectPrefix}.>"],
            DlqPrefix = $"{subjectPrefix}.dlq"
        };

        var bus = new NatsMessageBus(Options.Create(opts), NullLogger<NatsMessageBus>.Instance);
        await bus.ConnectAsync();

        var streamConfig = new StreamConfig(opts.StreamName, opts.StreamSubjects)
        {
            MaxAge = TimeSpan.FromMinutes(5)
        };
        await bus.JetStream.CreateOrUpdateStreamAsync(streamConfig);

        return (bus, subjectPrefix, opts);
    }

    [NatsFact]
    public async Task PublishAndSubscribe_RoundTripWithHeaders()
    {
        var (bus, prefix, _) = await CreateAndConnectAsync();
        await using var disposable = bus;

        var headers = MessageHeaders.NewRoot();
        var subject = $"{prefix}.events.story.{Guid.NewGuid():N}.created";
        var payload = new { story_id = "abc", title = "hello NATS", schema_version = 1 };

        await bus.PublishAsync(subject, payload, headers);

        var options = new SubscriptionOptions(DurableName: $"test-roundtrip-{Guid.NewGuid():N}");
        using var cts = new CancellationTokenSource(MessageTimeout);
        IncomingMessage? received = null;

        await foreach (var msg in bus.SubscribeAsync(subject, options, cts.Token))
        {
            received = msg;
            await msg.AckAsync(cts.Token);
            break;
        }

        Assert.NotNull(received);
        Assert.Equal(subject, received!.Subject);
        Assert.Equal(headers.MsgId, received.Headers.MsgId);
        Assert.Equal(headers.CorrelationId, received.Headers.CorrelationId);
        Assert.Equal(0, received.Headers.Generation);

        var body = JsonSerializer.Deserialize<JsonElement>(received.Payload.Span);
        Assert.Equal("hello NATS", body.GetProperty("title").GetString());
    }

    [NatsFact]
    public async Task Publish_GenerationExceeded_DropsMessage()
    {
        var (bus, prefix, _) = await CreateAndConnectAsync();
        await using var disposable = bus;

        var subject = $"{prefix}.events.story.{Guid.NewGuid():N}.created";
        var overLimit = new MessageHeaders(
            MsgId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            CausationId: Guid.NewGuid(),
            Generation: MessageHeaders.MaxGeneration + 1);

        await bus.PublishAsync(subject, new { }, overLimit);

        var options = new SubscriptionOptions(DurableName: $"test-genlimit-{Guid.NewGuid():N}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var count = 0;

        try
        {
            await foreach (var msg in bus.SubscribeAsync(subject, options, cts.Token))
            {
                count++;
                await msg.AckAsync(cts.Token);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Expected — timed out with no messages.
        }

        Assert.Equal(0, count);
    }

    [NatsFact]
    public async Task SubscribeAsync_DurableWithDottedName_SanitizesToDashes()
    {
        var (bus, prefix, _) = await CreateAndConnectAsync();
        await using var disposable = bus;

        var subject = $"{prefix}.events.story.{Guid.NewGuid():N}.created";
        await bus.PublishAsync(subject, new { x = "y" }, MessageHeaders.NewRoot());

        var options = new SubscriptionOptions(
            DurableName: $"andy.issues.test.{Guid.NewGuid():N}");
        using var cts = new CancellationTokenSource(MessageTimeout);
        IncomingMessage? received = null;

        await foreach (var msg in bus.SubscribeAsync(subject, options, cts.Token))
        {
            received = msg;
            await msg.AckAsync(cts.Token);
            break;
        }

        Assert.NotNull(received);
    }

    [NatsFact]
    public async Task MalformedHeaders_RouteToDlq()
    {
        var (bus, prefix, opts) = await CreateAndConnectAsync();
        await using var disposable = bus;

        // Publish directly via raw JetStream without the Andy-* headers
        // so the subscriber sees a message it cannot parse.
        var subject = $"{prefix}.events.story.{Guid.NewGuid():N}.created";
        var dlqSubject = $"{opts.DlqPrefix}.{subject}";
        var bytes = System.Text.Encoding.UTF8.GetBytes("""{"x":"y"}""");
        var ack = await bus.JetStream.PublishAsync(subject, bytes);
        Assert.Null(ack.Error);

        // Drive the bus subscription loop so the malformed message is
        // detected and DLQ-routed + acked. We can't use bus.Subscribe
        // on the DLQ side to verify because that same code would
        // re-DLQ it in a loop (the forwarded message still has no
        // Andy-* headers). Use raw JetStream for the verification side.
        using var subCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var driverTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in bus.SubscribeAsync(
                    subject,
                    new SubscriptionOptions($"dlq-driver-{Guid.NewGuid():N}"),
                    subCts.Token))
                {
                    await msg.AckAsync(subCts.Token);
                    break;
                }
            }
            catch (OperationCanceledException) { }
        });

        // Raw JetStream consumer on the DLQ subject. Does not apply the
        // NatsMessageBus header-parsing guard, so it will actually
        // surface the forwarded message.
        var dlqConsumerConfig = new ConsumerConfig($"dlq-verifier-{Guid.NewGuid():N}")
        {
            FilterSubject = dlqSubject,
            AckPolicy = NATS.Client.JetStream.Models.ConsumerConfigAckPolicy.Explicit
        };
        var dlqConsumer = await bus.JetStream.CreateOrUpdateConsumerAsync(
            opts.StreamName, dlqConsumerConfig);

        using var dlqCts = new CancellationTokenSource(MessageTimeout);
        string? observedSubject = null;
        await foreach (var jsMsg in dlqConsumer.ConsumeAsync<byte[]>(cancellationToken: dlqCts.Token))
        {
            observedSubject = jsMsg.Subject;
            await jsMsg.AckAsync(cancellationToken: dlqCts.Token);
            break;
        }

        await driverTask;

        Assert.Equal(dlqSubject, observedSubject);
    }
}
