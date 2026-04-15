// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Messaging;
using Andy.Issues.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Messaging;

public class InMemoryMessageBusTests
{
    [Theory]
    [InlineData("andy.issues.events.story.42.created", "andy.issues.events.story.42.created", true)]
    [InlineData("andy.issues.events.story.*.created", "andy.issues.events.story.42.created", true)]
    [InlineData("andy.issues.events.story.*.*", "andy.issues.events.story.42.created", true)]
    [InlineData("andy.issues.events.>", "andy.issues.events.story.42.created", true)]
    [InlineData("andy.issues.events.story.>", "andy.issues.events.story.42.created", true)]
    [InlineData("andy.issues.events.repository.>", "andy.issues.events.story.42.created", false)]
    [InlineData("andy.issues.events.story.*.created", "andy.issues.events.story.42.updated", false)]
    [InlineData("andy.issues.events.story.42", "andy.issues.events.story.42.created", false)]
    [InlineData("andy.issues.events.story.42.created.extra", "andy.issues.events.story.42.created", false)]
    public void MatchesSubject_HandlesNatsWildcards(string filter, string subject, bool expected)
    {
        Assert.Equal(expected, InMemoryMessageBus.MatchesSubject(filter, subject));
    }

    [Fact]
    public async Task Publish_ReachesSubscriberOnMatchingFilter()
    {
        var bus = new InMemoryMessageBus(NullLogger<InMemoryMessageBus>.Instance);
        bus.EnsureChannel("andy.issues.events.story.*.created");

        var received = new List<IncomingMessage>();
        var cts = new CancellationTokenSource();
        var subscriber = Task.Run(async () =>
        {
            await foreach (var msg in bus.SubscribeAsync(
                "andy.issues.events.story.*.created",
                new SubscriptionOptions("test"),
                cts.Token))
            {
                received.Add(msg);
                if (received.Count == 2) break;
            }
        });

        var headers1 = MessageHeaders.NewRoot();
        var headers2 = MessageHeaders.NewRoot();
        await bus.PublishAsync("andy.issues.events.story.1.created", new { storyId = 1 }, headers1);
        await bus.PublishAsync("andy.issues.events.story.2.created", new { storyId = 2 }, headers2);
        // Non-matching: should be ignored by the subscriber.
        await bus.PublishAsync("andy.issues.events.story.3.updated", new { storyId = 3 }, MessageHeaders.NewRoot());

        await Task.WhenAny(subscriber, Task.Delay(2000));
        cts.Cancel();

        Assert.Equal(2, received.Count);
        Assert.Equal("andy.issues.events.story.1.created", received[0].Subject);
        Assert.Equal("andy.issues.events.story.2.created", received[1].Subject);
    }

    [Fact]
    public async Task Publish_DropsMessageThatExceedsGenerationLimit()
    {
        var bus = new InMemoryMessageBus(NullLogger<InMemoryMessageBus>.Instance);
        bus.EnsureChannel("andy.issues.events.>");

        var over = new MessageHeaders(
            MsgId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            CausationId: null,
            Generation: MessageHeaders.MaxGeneration + 1);

        await bus.PublishAsync("andy.issues.events.story.1.created", new { }, over);

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var received = 0;
        try
        {
            await foreach (var _ in bus.SubscribeAsync(
                "andy.issues.events.>",
                new SubscriptionOptions("test"),
                cts.Token))
            {
                received++;
            }
        }
        catch (OperationCanceledException) { }

        Assert.Equal(0, received);
    }
}
