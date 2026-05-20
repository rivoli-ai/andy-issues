// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Andy.Issues.Application.Dtos;

// SP.0.4 (andy-issues#180 / conductor#1632 / conductor#1649) — tagged
// union describing a story's triage state as seen by Conductor's
// RefinementPanel. Wire shape:
//
//   { "kind": "NotTriaged" }
//   { "kind": "Triaging" }
//   { "kind": "Triaged",  "version": 1, "at": "2026-05-19T20:00:00Z" }
//   { "kind": "Obsolete", "version": 1, "at": "2026-05-19T20:00:00Z" }
//
// Note the `kind` discriminator is PascalCase to match the Swift Codable
// decoder in `Conductor/Features/WorkItem/Models/TriageState.swift`.
// We bypass the ambient JsonStringEnumConverter naming policy by
// hand-writing the converter — the rest of the DTO graph stays on the
// default System.Text.Json serializer.
[JsonConverter(typeof(StoryTriageStateDtoConverter))]
public abstract record StoryTriageStateDto
{
    public abstract string Kind { get; }

    public sealed record NotTriaged : StoryTriageStateDto
    {
        public override string Kind => "NotTriaged";
        public static NotTriaged Instance { get; } = new();
    }

    public sealed record Triaging : StoryTriageStateDto
    {
        public override string Kind => "Triaging";
        public static Triaging Instance { get; } = new();
    }

    public sealed record Triaged(int Version, DateTimeOffset At) : StoryTriageStateDto
    {
        public override string Kind => "Triaged";
    }

    public sealed record Obsolete(int Version, DateTimeOffset At) : StoryTriageStateDto
    {
        public override string Kind => "Obsolete";
    }
}

// Custom converter so:
//   1. The "kind" property is emitted with PascalCase values regardless
//      of the ambient JsonStringEnumConverter naming policy
//      (EventJson.Options uses SnakeCaseLower — that would otherwise
//      mangle "NotTriaged" into "not_triaged").
//   2. The discriminator is the JSON property name "kind" (camelCase
//      under the REST default Web options, PascalCase wouldn't render
//      because the *value* is the discriminator, not a property name).
//   3. Read path tolerates the same shape on both surfaces (REST clients
//      sometimes POST these on update bodies; the outbox payload also
//      round-trips through this converter).
internal sealed class StoryTriageStateDtoConverter : JsonConverter<StoryTriageStateDto>
{
    public override StoryTriageStateDto Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject for StoryTriageStateDto.");

        string? kind = null;
        int? version = null;
        DateTimeOffset? at = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected property name in StoryTriageStateDto.");

            var name = reader.GetString();
            reader.Read();

            switch (name)
            {
                case "kind":
                case "Kind":
                    kind = reader.GetString();
                    break;
                case "version":
                case "Version":
                    version = reader.TokenType == JsonTokenType.Null ? null : reader.GetInt32();
                    break;
                case "at":
                case "At":
                    at = reader.TokenType == JsonTokenType.Null ? null : reader.GetDateTimeOffset();
                    break;
                default:
                    // Forward-compat: skip unknown fields so future
                    // additions (e.g. an "agentId" field) don't 4xx.
                    reader.Skip();
                    break;
            }
        }

        return kind switch
        {
            "NotTriaged" => StoryTriageStateDto.NotTriaged.Instance,
            "Triaging" => StoryTriageStateDto.Triaging.Instance,
            "Triaged" when version is { } v && at is { } a => new StoryTriageStateDto.Triaged(v, a),
            "Obsolete" when version is { } v && at is { } a => new StoryTriageStateDto.Obsolete(v, a),
            _ => throw new JsonException(
                $"Unknown or malformed StoryTriageStateDto kind '{kind}'.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer, StoryTriageStateDto value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind);
        switch (value)
        {
            case StoryTriageStateDto.Triaged t:
                writer.WriteNumber("version", t.Version);
                writer.WriteString("at", t.At);
                break;
            case StoryTriageStateDto.Obsolete o:
                writer.WriteNumber("version", o.Version);
                writer.WriteString("at", o.At);
                break;
        }
        writer.WriteEndObject();
    }
}
