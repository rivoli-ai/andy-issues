// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Interfaces;

/// <summary>
/// Server-side view of an MCP server config including secret fields. This record is
/// only used on the sandbox-injection path inside the service layer — it intentionally
/// bypasses <see cref="Andy.Issues.Application.Dtos.McpServerConfigDto"/>, which masks
/// EnvironmentJson and HeadersJson for outbound API responses. Never return an instance
/// of this record to an HTTP/gRPC/MCP client.
/// </summary>
public record McpServerConfigFull(
    Guid Id,
    string Name,
    string? Description,
    string Type,
    bool Enabled,
    string? Command,
    string? ArgumentsJson,
    string? EnvironmentJson,
    string? Url,
    string? HeadersJson);

public interface IMcpConfigService
{
    /// <summary>
    /// Returns every enabled MCP server config that the given user can see — personal
    /// configs (<c>OwnerUserId == userId</c>) plus every shared config. Results include
    /// unmasked env/headers JSON and MUST NOT be surfaced to outbound API responses.
    /// </summary>
    Task<IReadOnlyList<McpServerConfigFull>> GetEnabledForUserAsync(
        string userId,
        CancellationToken ct = default);
}
