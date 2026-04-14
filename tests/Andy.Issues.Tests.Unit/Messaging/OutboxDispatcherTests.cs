// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Andy.Issues.Tests.Unit.Messaging;

public class OutboxDispatcherTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public OutboxDispatcherTests()
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

    // Helper to get a dispatcher with a given bus and optionally customized options.
    private (OutboxDispatcher Dispatcher, RecordingBus Bus) BuildDispatcher(
        OutboxDispatcherOptions? options = null, bool throwOnPublish = false)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(_options));
        var bus = new RecordingBus { ThrowOnPublish = throwOnPublish };
        services.AddSingleton<IMessageBus>(bus);
        var sp = services.BuildServiceProvider();

        var dispatcher = new OutboxDispatcher(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboxDispatcher>.Instance,
            Options.Create(options ?? new OutboxDispatcherOptions { PollInterval = TimeSpan.FromMilliseconds(50) }));
        return (dispatcher, bus);
    }

    [Fact]
    public async Task DrainOnceAsync_PublishesInCreatedAtOrder_AndMarksPublishedAt()
    {
        var now = DateTimeOffset.UtcNow;
        await using (var ctx = NewContext())
        {
            ctx.Outbox.AddRange(
                new OutboxEntry { Id = Guid.NewGuid(), Subject = "s.2", CreatedAt = now.AddSeconds(2), CorrelationId = Guid.NewGuid(), PayloadJson = "{\"x\":2}" },
                new OutboxEntry { Id = Guid.NewGuid(), Subject = "s.1", CreatedAt = now.AddSeconds(1), CorrelationId = Guid.NewGuid(), PayloadJson = "{\"x\":1}" },
                new OutboxEntry { Id = Guid.NewGuid(), Subject = "s.3", CreatedAt = now.AddSeconds(3), CorrelationId = Guid.NewGuid(), PayloadJson = "{\"x\":3}" });
            await ctx.SaveChangesAsync();
        }

        var (dispatcher, bus) = BuildDispatcher();
        var drained = await dispatcher.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(3, drained);
        Assert.Equal(new[] { "s.1", "s.2", "s.3" }, bus.Published.Select(p => p.Subject).ToArray());

        await using var verify = NewContext();
        var persisted = await verify.Outbox.OrderBy(e => e.CreatedAt).ToListAsync();
        Assert.All(persisted, e => Assert.NotNull(e.PublishedAt));
        Assert.All(persisted, e => Assert.Null(e.LastError));
    }

    [Fact]
    public async Task DrainOnceAsync_IncrementsAttempts_AndRecordsErrorOnFailure()
    {
        await using (var ctx = NewContext())
        {
            ctx.Outbox.Add(new OutboxEntry
            {
                Id = Guid.NewGuid(),
                Subject = "s.boom",
                CorrelationId = Guid.NewGuid(),
                PayloadJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        var (dispatcher, _) = BuildDispatcher(throwOnPublish: true);
        var drained = await dispatcher.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(1, drained);

        await using var verify = NewContext();
        var row = await verify.Outbox.SingleAsync();
        Assert.Null(row.PublishedAt);
        Assert.Equal(1, row.AttemptCount);
        Assert.NotNull(row.LastAttemptAt);
        Assert.Contains("boom", row.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DrainOnceAsync_SkipsRowsWithinBackoffWindow()
    {
        await using (var ctx = NewContext())
        {
            ctx.Outbox.Add(new OutboxEntry
            {
                Id = Guid.NewGuid(),
                Subject = "s.backoff",
                CorrelationId = Guid.NewGuid(),
                PayloadJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow,
                AttemptCount = 3,
                // Just failed: BackoffBase * 2^2 = 4s, so definitely within the window.
                LastAttemptAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        var (dispatcher, bus) = BuildDispatcher(new OutboxDispatcherOptions
        {
            BackoffBase = TimeSpan.FromSeconds(1),
            BackoffMax = TimeSpan.FromMinutes(5),
        });

        var drained = await dispatcher.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(0, drained);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task DrainOnceAsync_ReturnsZeroWhenEmpty()
    {
        var (dispatcher, _) = BuildDispatcher();
        Assert.Equal(0, await dispatcher.DrainOnceAsync(CancellationToken.None));
    }

    private sealed class RecordingBus : IMessageBus
    {
        public bool ThrowOnPublish { get; set; }
        public ConcurrentQueue<(string Subject, object Payload, MessageHeaders Headers)> Published { get; } = new();

        public Task PublishAsync(string subject, object payload, MessageHeaders headers, CancellationToken ct = default)
        {
            if (ThrowOnPublish)
            {
                throw new InvalidOperationException("boom");
            }

            Published.Enqueue((subject, payload, headers));
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<IncomingMessage> SubscribeAsync(string subjectFilter, SubscriptionOptions options, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
