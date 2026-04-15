// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Messaging;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Messaging;
using Andy.Issues.Infrastructure.Messaging.Nats;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.JetStream.Models;
using Xunit;

namespace Andy.Issues.Tests.Integration.Messaging;

// End-to-end outbox → NATS test. Covers the path that matters in
// production: service writes an OutboxEntry, the dispatcher drains
// the row through NatsMessageBus, a durable subscriber reads the
// forwarded message with its ADR 0001 headers intact, and the outbox
// row is marked PublishedAt.
//
// Env-gated by [NatsFact]. Run locally with:
//   docker compose up -d nats
//   ANDY_ISSUES_TEST_NATS=true dotnet test tests/Andy.Issues.Tests.Integration
public class OutboxDispatcherNatsTests
{
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);

    private static string NatsUrl =>
        Environment.GetEnvironmentVariable("NATS_URL")
        ?? Environment.GetEnvironmentVariable("Messaging__Nats__Url")
        ?? "nats://localhost:4222";

    [NatsFact]
    public async Task DispatcherDrain_PublishesOutboxRowToNats_AndMarksPublished()
    {
        // A SQLite-backed AppDbContext gives the dispatcher a real EF
        // query path to exercise. In-memory provider would work but
        // would not match production semantics as closely.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        using (var boot = new AppDbContext(dbOptions))
        {
            boot.Database.EnsureCreated();
        }

        // Each test carves its own subject prefix + stream so parallel
        // runs or stale state from prior runs can't cross-talk.
        var testId = Guid.NewGuid().ToString("N");
        var subjectPrefix = $"andy.test-{testId}";
        var natsOptions = new NatsOptions
        {
            Url = NatsUrl,
            StreamName = $"ANDY_TEST_{testId}",
            StreamSubjects = [$"{subjectPrefix}.>"],
            DlqPrefix = $"{subjectPrefix}.dlq",
        };

        var bus = new NatsMessageBus(
            Options.Create(natsOptions), NullLogger<NatsMessageBus>.Instance);
        await using var busDisposer = bus;
        await bus.ConnectAsync();
        await bus.JetStream.CreateOrUpdateStreamAsync(
            new StreamConfig(natsOptions.StreamName, natsOptions.StreamSubjects)
            {
                MaxAge = TimeSpan.FromMinutes(5),
            });

        // Seed an outbox row with an ADR-0001-shaped payload. CorrelationId
        // is seeded explicitly so headers survive the round-trip intact.
        var storyId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var subject = $"{subjectPrefix}.events.story.{storyId:N}.created";
        await using (var db = new AppDbContext(dbOptions))
        {
            db.Outbox.Add(new OutboxEntry
            {
                Id = entryId,
                Subject = subject,
                PayloadType = typeof(object).FullName,
                PayloadJson = $"{{\"story_id\":\"{storyId:N}\",\"title\":\"nats e2e\",\"schema_version\":1}}",
                CorrelationId = correlationId,
                CausationId = null,
                Generation = 0,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // Build the dispatcher manually so we can Drain on demand — a
        // hosted-service + poll-loop shape would work too but the direct
        // call keeps the test deterministic.
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(dbOptions));
        services.AddSingleton<IMessageBus>(bus);
        using var sp = services.BuildServiceProvider();
        var dispatcher = new OutboxDispatcher(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboxDispatcher>.Instance,
            Options.Create(new OutboxDispatcherOptions { PollInterval = TimeSpan.FromMilliseconds(50) }));

        var drained = await dispatcher.DrainOnceAsync(CancellationToken.None);
        Assert.Equal(1, drained);

        // Subscribe after the publish to prove durability: JetStream
        // replays the message from the beginning of the stream for a
        // fresh durable consumer.
        using var cts = new CancellationTokenSource(MessageTimeout);
        IncomingMessage? received = null;
        await foreach (var msg in bus.SubscribeAsync(
            subject,
            new SubscriptionOptions($"test-outbox-{testId}"),
            cts.Token))
        {
            received = msg;
            await msg.AckAsync(cts.Token);
            break;
        }

        Assert.NotNull(received);
        Assert.Equal(subject, received!.Subject);
        Assert.Equal(entryId, received.Headers.MsgId);
        Assert.Equal(correlationId, received.Headers.CorrelationId);
        Assert.Null(received.Headers.CausationId);
        Assert.Equal(0, received.Headers.Generation);

        await using (var verify = new AppDbContext(dbOptions))
        {
            var row = await verify.Outbox.AsNoTracking().SingleAsync(e => e.Id == entryId);
            Assert.NotNull(row.PublishedAt);
            Assert.Null(row.LastError);
            Assert.Equal(0, row.AttemptCount);
        }
    }
}
