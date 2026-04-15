// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Messaging;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Messaging;

// Verifies the end-to-end path:
//   1) Something writes an OutboxEntry through AppDbContext.
//   2) The OutboxDispatcher hosted service drains it.
//   3) PublishedAt is set.
// This proves Program.cs actually registers the dispatcher and wires its
// AppDbContext + IMessageBus dependencies correctly. Uses the standard
// test harness (InMemoryDatabase + InMemoryMessageBus).
public class OutboxRoundTripTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public OutboxRoundTripTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _ = _factory.CreateClient(); // force host start
    }

    [Fact]
    public async Task Dispatcher_DrainsPendingOutboxRow_AndMarksPublished()
    {
        var entryId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Outbox.Add(new OutboxEntry
            {
                Id = entryId,
                Subject = "andy.issues.events.story.test.created",
                CorrelationId = Guid.NewGuid(),
                PayloadJson = "{\"story_id\":\"test\",\"schema_version\":1}",
                PayloadType = typeof(object).FullName,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // Poll up to ~5 s for the background dispatcher to pick it up.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        OutboxEntry? row = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            row = await db.Outbox.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entryId);
            if (row?.PublishedAt is not null) break;
            await Task.Delay(100);
        }

        Assert.NotNull(row);
        Assert.NotNull(row!.PublishedAt);
        Assert.Null(row.LastError);
    }

    [Fact]
    public void Bus_IsResolvable_FromRoot()
    {
        using var scope = _factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        Assert.NotNull(bus);
    }
}
