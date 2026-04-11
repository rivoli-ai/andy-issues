// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Domain.Entities;

public class McpServerConfig
{
    public Guid Id { get; set; }
    public string? OwnerUserId { get; set; }
    public bool IsShared { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public McpServerType Type { get; set; }
    public bool Enabled { get; set; } = true;

    public string? Command { get; set; }
    public string? ArgumentsJson { get; set; }
    public string? EnvironmentJson { get; set; }

    public string? Url { get; set; }
    public string? HeadersJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
