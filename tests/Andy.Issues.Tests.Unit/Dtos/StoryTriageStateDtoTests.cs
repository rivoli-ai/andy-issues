// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Messaging;
using Xunit;

namespace Andy.Issues.Tests.Unit.Dtos;

// SP.0.4 (andy-issues#180 / conductor#1649) — verify the tagged-union
// wire shape Conductor's `TriageState` decoder consumes. Critical
// invariants:
//   • `kind` is always PascalCase (NotTriaged / Triaging / Triaged /
//     Obsolete) regardless of the ambient JsonStringEnumConverter
//     naming policy.
//   • Triaged + Obsolete carry version (int) + at (ISO-8601 timestamp).
//   • NotTriaged + Triaging carry no payload beyond `kind`.
//   • The shape round-trips on both EventJson.Options (snake_case) and
//     the REST default (Web / camelCase) options because the
//     discriminator value is a quoted string, not a member name.
public class StoryTriageStateDtoTests
{
    private static readonly JsonSerializerOptions Rest = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions Events = EventJson.Options;

    [Theory]
    [InlineData("not triaged")]
    [InlineData("triaging")]
    public void NotTriaged_And_Triaging_SerializeToKindOnly(string label)
    {
        StoryTriageStateDto value = label switch
        {
            "not triaged" => StoryTriageStateDto.NotTriaged.Instance,
            _ => StoryTriageStateDto.Triaging.Instance
        };

        var json = JsonSerializer.Serialize(value, Rest);
        Assert.DoesNotContain("version", json);
        Assert.DoesNotContain("at", json);
        Assert.Contains("\"kind\"", json);
    }

    [Fact]
    public void NotTriaged_KindIsPascalCase_OnRestOptions()
    {
        var json = JsonSerializer.Serialize<StoryTriageStateDto>(
            StoryTriageStateDto.NotTriaged.Instance, Rest);
        Assert.Contains("\"NotTriaged\"", json);
    }

    [Fact]
    public void Triaging_KindIsPascalCase_OnEventOptions()
    {
        // EventJson.Options uses SnakeCaseLower; the custom converter
        // must bypass it for the "kind" value because Conductor decodes
        // PascalCase only.
        var json = JsonSerializer.Serialize<StoryTriageStateDto>(
            StoryTriageStateDto.Triaging.Instance, Events);
        Assert.Contains("\"Triaging\"", json);
    }

    [Fact]
    public void Triaged_SerializesVersionAndAt()
    {
        var at = new DateTimeOffset(2026, 5, 19, 20, 0, 0, TimeSpan.Zero);
        var value = new StoryTriageStateDto.Triaged(Version: 3, At: at);
        var json = JsonSerializer.Serialize<StoryTriageStateDto>(value, Rest);

        Assert.Contains("\"kind\":\"Triaged\"", json);
        Assert.Contains("\"version\":3", json);
        Assert.Contains("2026-05-19T20:00:00", json);
    }

    [Fact]
    public void Obsolete_SerializesVersionAndAt()
    {
        var at = new DateTimeOffset(2026, 5, 19, 20, 0, 0, TimeSpan.Zero);
        var value = new StoryTriageStateDto.Obsolete(Version: 7, At: at);
        var json = JsonSerializer.Serialize<StoryTriageStateDto>(value, Rest);

        Assert.Contains("\"kind\":\"Obsolete\"", json);
        Assert.Contains("\"version\":7", json);
    }

    [Theory]
    [InlineData("{\"kind\":\"NotTriaged\"}", "NotTriaged")]
    [InlineData("{\"kind\":\"Triaging\"}", "Triaging")]
    public void NotTriaged_And_Triaging_RoundTripFromJson(string json, string expectedKind)
    {
        var value = JsonSerializer.Deserialize<StoryTriageStateDto>(json, Rest);
        Assert.NotNull(value);
        Assert.Equal(expectedKind, value!.Kind);
    }

    [Fact]
    public void Triaged_RoundTripsFromJson()
    {
        var json = "{\"kind\":\"Triaged\",\"version\":4,\"at\":\"2026-05-19T20:00:00Z\"}";
        var value = JsonSerializer.Deserialize<StoryTriageStateDto>(json, Rest);
        Assert.IsType<StoryTriageStateDto.Triaged>(value);
        var t = (StoryTriageStateDto.Triaged)value!;
        Assert.Equal(4, t.Version);
        Assert.Equal(new DateTimeOffset(2026, 5, 19, 20, 0, 0, TimeSpan.Zero), t.At);
    }

    [Fact]
    public void Obsolete_RoundTripsFromJson()
    {
        var json = "{\"kind\":\"Obsolete\",\"version\":2,\"at\":\"2026-05-19T19:00:00Z\"}";
        var value = JsonSerializer.Deserialize<StoryTriageStateDto>(json, Rest);
        Assert.IsType<StoryTriageStateDto.Obsolete>(value);
    }

    [Fact]
    public void ForwardCompat_UnknownFieldsAreIgnored()
    {
        var json = "{\"kind\":\"NotTriaged\",\"futureField\":\"ignored\"}";
        var value = JsonSerializer.Deserialize<StoryTriageStateDto>(json, Rest);
        Assert.IsType<StoryTriageStateDto.NotTriaged>(value);
    }

    [Fact]
    public void UnknownKind_Throws()
    {
        var json = "{\"kind\":\"Bogus\"}";
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<StoryTriageStateDto>(json, Rest));
    }

    [Fact]
    public void RoundTrip_Triaged_OnEventOptions()
    {
        var at = new DateTimeOffset(2026, 5, 19, 20, 0, 0, TimeSpan.Zero);
        var value = new StoryTriageStateDto.Triaged(Version: 1, At: at);

        var json = JsonSerializer.Serialize<StoryTriageStateDto>(value, Events);
        var roundTripped = JsonSerializer.Deserialize<StoryTriageStateDto>(json, Events);

        Assert.IsType<StoryTriageStateDto.Triaged>(roundTripped);
        Assert.Equal(value, roundTripped);
    }
}
