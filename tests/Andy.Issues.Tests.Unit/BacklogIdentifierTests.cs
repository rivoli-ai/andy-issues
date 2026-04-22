// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application;
using Andy.Issues.Domain.Enums;
using Xunit;

namespace Andy.Issues.Tests.Unit;

// AH1 — BacklogIdentifier parser contract.
//
// The parser is the single ingress filter for every public route
// that accepts a `{id}` path segment. These tests lock down the
// accepted shapes so a future change that relaxes the rules (e.g.
// leading zeros, whitespace-tolerant GUIDs) has to reckon with
// the expected downstream behaviour explicitly.
public class BacklogIdentifierTests
{
    [Fact]
    public void Parse_guid_round_trips()
    {
        var id = Guid.NewGuid();
        var parsed = BacklogIdentifier.Parse(id.ToString());

        Assert.Equal(id, parsed.Id);
        Assert.Null(parsed.ExpectedType);
        Assert.Null(parsed.Seq);
    }

    [Theory]
    [InlineData("EPIC-42", BacklogEntityType.Epic, 42L)]
    [InlineData("epic-7", BacklogEntityType.Epic, 7L)]
    [InlineData("FEAT-1", BacklogEntityType.Feature, 1L)]
    [InlineData("feat-99", BacklogEntityType.Feature, 99L)]
    [InlineData("STORY-13", BacklogEntityType.Story, 13L)]
    [InlineData("story-2", BacklogEntityType.Story, 2L)]
    public void Parse_display_id_identifies_type_and_seq(
        string input, BacklogEntityType expectedType, long expectedSeq)
    {
        var parsed = BacklogIdentifier.Parse(input);

        Assert.Null(parsed.Id);
        Assert.Equal(expectedType, parsed.ExpectedType);
        Assert.Equal(expectedSeq, parsed.Seq);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("EPIC-")]
    [InlineData("-42")]
    [InlineData("UNKNOWN-1")]
    [InlineData("EPIC-abc")]
    [InlineData("EPIC-0")]          // zero is reserved — allocator starts at 1
    [InlineData("EPIC--1")]
    [InlineData("EPIC-1.5")]
    [InlineData("not-a-guid")]
    public void Parse_rejects_malformed_input(string? input)
    {
        var parsed = BacklogIdentifier.Parse(input);

        Assert.Null(parsed.Id);
        Assert.Null(parsed.ExpectedType);
        Assert.Null(parsed.Seq);
    }

    [Fact]
    public void Parse_trims_whitespace()
    {
        var parsed = BacklogIdentifier.Parse("  STORY-42  ");

        Assert.Equal(BacklogEntityType.Story, parsed.ExpectedType);
        Assert.Equal(42L, parsed.Seq);
    }
}
