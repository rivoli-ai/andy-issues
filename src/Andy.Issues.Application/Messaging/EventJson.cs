// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Andy.Issues.Application.Messaging;

// Canonical JSON options for every ADR 0001 event payload. Publishers
// (Story 15.3–15.5) and consumers (IncomingMessage.Deserialize) use these
// so snake_case / camelCase drift cannot silently deserialize to default
// values. Enum values serialize as lowercase-snake strings so adding a
// new enum member downstream does not shift ordinals.
public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
