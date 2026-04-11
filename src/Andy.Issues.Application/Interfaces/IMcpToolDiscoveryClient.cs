// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;

namespace Andy.Issues.Application.Interfaces;

public record McpToolDescriptor(
    string Name,
    string? Description,
    JsonElement? InputSchema);

public enum McpToolDiscoveryOutcome
{
    Ok = 0,
    Timeout = 1,
    HttpError = 2,
    MalformedResponse = 3
}

public record McpToolDiscoveryResult(
    McpToolDiscoveryOutcome Outcome,
    IReadOnlyList<McpToolDescriptor>? Tools,
    string? Error);

/// <summary>
/// Performs the MCP JSON-RPC handshake against a remote MCP server and returns its
/// tools list. This is a pure HTTP seam so tests can stub it without standing up a
/// real MCP process.
/// </summary>
public interface IMcpToolDiscoveryClient
{
    Task<McpToolDiscoveryResult> DiscoverAsync(
        string url,
        string? headersJson,
        CancellationToken ct = default);
}
