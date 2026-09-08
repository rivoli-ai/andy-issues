// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

// Intentionally a class, not a record: ToString must never print the API key.
public sealed class AiConfigDto
{
    public required string Provider { get; init; }
    public required string ApiKey { get; init; }
    public required string Model { get; init; }
    public string? BaseUrl { get; init; }
}
