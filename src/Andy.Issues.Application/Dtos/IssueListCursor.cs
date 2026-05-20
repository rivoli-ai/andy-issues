// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Buffers.Text;
using System.Text;

namespace Andy.Issues.Application.Dtos;

// #187 — opaque pagination cursor for `GET /api/issues`. Encodes the
// `(CreatedAt, Id)` composite of the last row returned in the current
// page so subsequent pages can resume past it with a stable sort
// (`OrderBy CreatedAt DESC, Id DESC`). Base64 so the cursor is URL-
// safe; format is internal — callers MUST round-trip whatever the
// server returned without parsing it.
public static class IssueListCursor
{
    public static string Encode(DateTimeOffset createdAt, Guid id)
    {
        // ISO 8601 round-trip format preserves nanoseconds and offset,
        // so the next page picks up exactly past the previous row.
        var raw = $"{createdAt.ToString("O")}|{id:D}";
        var bytes = Encoding.UTF8.GetBytes(raw);
        return Convert.ToBase64String(bytes);
    }

    public static bool TryDecode(string? cursor, out DateTimeOffset createdAt, out Guid id)
    {
        createdAt = default;
        id = default;

        if (string.IsNullOrWhiteSpace(cursor)) return false;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(cursor);
        }
        catch (FormatException)
        {
            return false;
        }

        var raw = Encoding.UTF8.GetString(bytes);
        var pipe = raw.IndexOf('|');
        if (pipe <= 0 || pipe >= raw.Length - 1) return false;

        if (!DateTimeOffset.TryParse(raw[..pipe], null,
                System.Globalization.DateTimeStyles.RoundtripKind, out createdAt))
        {
            return false;
        }

        return Guid.TryParse(raw[(pipe + 1)..], out id);
    }
}
