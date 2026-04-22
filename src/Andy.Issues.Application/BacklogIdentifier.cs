// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Globalization;
using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Application;

/// <summary>
/// Parses route / request identifiers that may arrive as either a
/// GUID (canonical primary key) or an AH1 short display id
/// (<c>EPIC-42</c> / <c>FEAT-7</c> / <c>STORY-13</c>). Centralised
/// so every controller + service resolver agrees on the accepted
/// shape and bound entity type.
/// </summary>
public static class BacklogIdentifier
{
    public readonly record struct ParseResult(
        Guid? Id,
        BacklogEntityType? ExpectedType,
        long? Seq);

    public static ParseResult Parse(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return default;

        string trimmed = identifier.Trim();

        if (Guid.TryParse(trimmed, out Guid id))
            return new ParseResult(id, null, null);

        int dash = trimmed.IndexOf('-');
        if (dash <= 0 || dash == trimmed.Length - 1)
            return default;

        string prefix = trimmed[..dash];
        string rest = trimmed[(dash + 1)..];

        BacklogEntityType? type = prefix.ToUpperInvariant() switch
        {
            "EPIC" => BacklogEntityType.Epic,
            "FEAT" => BacklogEntityType.Feature,
            "STORY" => BacklogEntityType.Story,
            _ => null
        };
        if (type is null) return default;

        if (!long.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seq) || seq < 1)
            return default;

        return new ParseResult(null, type, seq);
    }
}
