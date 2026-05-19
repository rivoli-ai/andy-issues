// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.RegularExpressions;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Services;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

// SP.7.1 (andy-issues#181 / conductor#1627) — proves the stable
// content-hash contract that downstream services rely on to detect
// drift after a re-import.
public class StoryContentHasherTests
{
    [Fact]
    public void Compute_ReturnsLowercaseSha256Hex()
    {
        var hash = StoryContentHasher.Compute(
            title: "Title",
            body: "Body",
            labels: new[] { "bug" },
            acceptanceCriteria: "Given X");

        Assert.Equal(64, hash.Length);
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), hash);
    }

    [Fact]
    public void Compute_FromEntity_MatchesFromFields()
    {
        var story = new UserStory
        {
            Title = "Add login",
            Description = "Users need to log in.",
            Labels = new List<string> { "auth", "frontend" },
            AcceptanceCriteria = "Given valid creds\nWhen submit\nThen logged in"
        };

        var fromEntity = StoryContentHasher.Compute(story);
        var fromFields = StoryContentHasher.Compute(
            "Add login",
            "Users need to log in.",
            new[] { "auth", "frontend" },
            "Given valid creds\nWhen submit\nThen logged in");

        Assert.Equal(fromEntity, fromFields);
    }

    [Fact]
    public void Compute_TitleWhitespaceTrimmed()
    {
        var a = StoryContentHasher.Compute("Add login", null, null, null);
        var b = StoryContentHasher.Compute("  Add login  ", null, null, null);
        var c = StoryContentHasher.Compute("\n\tAdd login\n", null, null, null);
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void Compute_BodyWhitespaceTrimmed()
    {
        var a = StoryContentHasher.Compute("T", "Body content", null, null);
        var b = StoryContentHasher.Compute("T", "  Body content\n\n", null, null);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_LineEndingsNormalized()
    {
        var lf = StoryContentHasher.Compute("T", "line 1\nline 2", null, null);
        var crlf = StoryContentHasher.Compute("T", "line 1\r\nline 2", null, null);
        var cr = StoryContentHasher.Compute("T", "line 1\rline 2", null, null);
        Assert.Equal(lf, crlf);
        Assert.Equal(lf, cr);
    }

    [Fact]
    public void Compute_LabelsAreSortedAndDeduped()
    {
        var a = StoryContentHasher.Compute("T", null, new[] { "bug", "frontend", "auth" }, null);
        var b = StoryContentHasher.Compute("T", null, new[] { "auth", "frontend", "bug" }, null);
        var c = StoryContentHasher.Compute("T", null, new[] { "frontend", "auth", "auth", "bug" }, null);
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void Compute_LabelsAreCaseSensitive()
    {
        // GitHub treats "bug" and "Bug" as distinct labels — preserve
        // that semantic in the hash so a case rename is detected as drift.
        var lower = StoryContentHasher.Compute("T", null, new[] { "bug" }, null);
        var mixed = StoryContentHasher.Compute("T", null, new[] { "Bug" }, null);
        Assert.NotEqual(lower, mixed);
    }

    [Fact]
    public void Compute_LabelWhitespaceTrimmed()
    {
        var a = StoryContentHasher.Compute("T", null, new[] { "bug", "auth" }, null);
        var b = StoryContentHasher.Compute("T", null, new[] { "  bug  ", "\tauth\n" }, null);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_EmptyLabelsDropped()
    {
        var a = StoryContentHasher.Compute("T", null, new[] { "bug" }, null);
        var b = StoryContentHasher.Compute("T", null, new[] { "bug", "", "   " }, null);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_AcceptanceCriteriaOrderMatters()
    {
        // Per spec: AC are joined "in order" — reordering them IS
        // semantic and SHOULD change the hash.
        var ordered = StoryContentHasher.Compute("T", null, null,
            "Given A\nGiven B\nGiven C");
        var reordered = StoryContentHasher.Compute("T", null, null,
            "Given C\nGiven A\nGiven B");
        Assert.NotEqual(ordered, reordered);
    }

    [Fact]
    public void Compute_AcceptanceCriteriaTrailingWhitespacePerLineIgnored()
    {
        var a = StoryContentHasher.Compute("T", null, null,
            "Given A\nGiven B");
        var b = StoryContentHasher.Compute("T", null, null,
            "Given A   \nGiven B\t");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_AcceptanceCriteriaTrailingBlankLinesStripped()
    {
        var a = StoryContentHasher.Compute("T", null, null,
            "Given A\nGiven B");
        var b = StoryContentHasher.Compute("T", null, null,
            "Given A\nGiven B\n\n\n");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_TitleEditChangesHash()
    {
        var a = StoryContentHasher.Compute("Add login", "B", null, null);
        var b = StoryContentHasher.Compute("Add logout", "B", null, null);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_BodyEditChangesHash()
    {
        var a = StoryContentHasher.Compute("T", "Body v1", null, null);
        var b = StoryContentHasher.Compute("T", "Body v2", null, null);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_LabelAddedChangesHash()
    {
        var a = StoryContentHasher.Compute("T", null, new[] { "bug" }, null);
        var b = StoryContentHasher.Compute("T", null, new[] { "bug", "regression" }, null);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_AcceptanceCriteriaAddedChangesHash()
    {
        var a = StoryContentHasher.Compute("T", null, null, "Given A");
        var b = StoryContentHasher.Compute("T", null, null, "Given A\nGiven B");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_NullVsEmptyAreEquivalent()
    {
        var nulls = StoryContentHasher.Compute("T", null, null, null);
        var empties = StoryContentHasher.Compute("T", "", Array.Empty<string>(), "");
        Assert.Equal(nulls, empties);
    }

    [Fact]
    public void Compute_IsDeterministicAcrossCalls()
    {
        // 100 calls with the same inputs all produce the same hash.
        var first = StoryContentHasher.Compute(
            "Add login",
            "Users need to log in.",
            new[] { "auth", "frontend" },
            "Given valid creds\nWhen submit\nThen logged in");

        for (var i = 0; i < 100; i++)
        {
            var again = StoryContentHasher.Compute(
                "Add login",
                "Users need to log in.",
                new[] { "auth", "frontend" },
                "Given valid creds\nWhen submit\nThen logged in");
            Assert.Equal(first, again);
        }
    }

    [Fact]
    public void Compute_DoesNotCollideAcrossFieldBoundaries()
    {
        // Without per-section delimiters, "title:Aaa, body:Bbb" could
        // accidentally hash the same as "title:Aaab, body:bb". The
        // section prefix + NUL separator prevents that — proves the
        // framing is doing its job.
        var a = StoryContentHasher.Compute("Aaa", "Bbb", null, null);
        var b = StoryContentHasher.Compute("Aaab", "bb", null, null);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_FieldsAreNotInterchangeable()
    {
        // A label "Add login" should not collide with a title "Add login".
        var titled = StoryContentHasher.Compute("Add login", null, null, null);
        var labelled = StoryContentHasher.Compute("", null, new[] { "Add login" }, null);
        Assert.NotEqual(titled, labelled);
    }

    [Fact]
    public void Canonicalize_IsHumanReadable()
    {
        // Diagnostic surface: the canonical string should embed the
        // section names so a developer staring at it can see why two
        // stories hashed differently.
        var canonical = StoryContentHasher.Canonicalize(
            "T", "B", new[] { "bug" }, "AC");
        Assert.Contains("title:", canonical);
        Assert.Contains("body:", canonical);
        Assert.Contains("labels:", canonical);
        Assert.Contains("acceptance_criteria:", canonical);
    }
}
