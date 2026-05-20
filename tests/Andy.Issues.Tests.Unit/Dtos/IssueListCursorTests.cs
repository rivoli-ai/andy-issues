// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Xunit;

namespace Andy.Issues.Tests.Unit.Dtos;

// #187 — cursor codec round-trip and malformed-input handling. The
// cursor format is internal; the contract is that any value returned
// by <see cref="IssueListCursor.Encode"/> decodes back to the same
// timestamp + id, and any garbage decodes to <c>false</c> without
// throwing.
public class IssueListCursorTests
{
    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        var encoded = IssueListCursor.Encode(createdAt, id);
        Assert.True(IssueListCursor.TryDecode(encoded, out var roundCreatedAt, out var roundId));
        Assert.Equal(createdAt, roundCreatedAt);
        Assert.Equal(id, roundId);
    }

    [Fact]
    public void Decode_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(IssueListCursor.TryDecode(null, out _, out _));
        Assert.False(IssueListCursor.TryDecode("", out _, out _));
        Assert.False(IssueListCursor.TryDecode("   ", out _, out _));
    }

    [Fact]
    public void Decode_NotBase64_ReturnsFalse()
    {
        Assert.False(IssueListCursor.TryDecode("!!!not-base64!!!", out _, out _));
    }

    [Fact]
    public void Decode_Base64ButWrongFormat_ReturnsFalse()
    {
        var bogus = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not|a|valid|cursor"));
        Assert.False(IssueListCursor.TryDecode(bogus, out _, out _));
    }
}
