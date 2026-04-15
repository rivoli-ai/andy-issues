// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Messaging;
using Xunit;

namespace Andy.Issues.Tests.Unit.Messaging;

public class MessageHeadersTests
{
    [Fact]
    public void NewRoot_UsesItsOwnMsgIdAsCorrelation_WhenNoneProvided()
    {
        var headers = MessageHeaders.NewRoot();

        Assert.Equal(headers.MsgId, headers.CorrelationId);
        Assert.Null(headers.CausationId);
        Assert.Equal(0, headers.Generation);
        Assert.False(headers.ExceedsGenerationLimit);
    }

    [Fact]
    public void NewRoot_PreservesSuppliedCorrelationId()
    {
        var existing = Guid.NewGuid();
        var headers = MessageHeaders.NewRoot(existing);

        Assert.Equal(existing, headers.CorrelationId);
        Assert.NotEqual(existing, headers.MsgId);
    }

    [Fact]
    public void Follow_IncrementsGenerationAndSetsCausation()
    {
        var parent = MessageHeaders.NewRoot();
        var child = MessageHeaders.Follow(parent);

        Assert.Equal(parent.MsgId, child.CausationId);
        Assert.Equal(parent.CorrelationId, child.CorrelationId);
        Assert.Equal(parent.Generation + 1, child.Generation);
        Assert.NotEqual(parent.MsgId, child.MsgId);
    }

    [Fact]
    public void ExceedsGenerationLimit_TrueAtElevenAndBeyond()
    {
        var atLimit = new MessageHeaders(Guid.NewGuid(), Guid.NewGuid(), null, MessageHeaders.MaxGeneration);
        var past = atLimit with { Generation = MessageHeaders.MaxGeneration + 1 };

        Assert.False(atLimit.ExceedsGenerationLimit);
        Assert.True(past.ExceedsGenerationLimit);
    }
}
