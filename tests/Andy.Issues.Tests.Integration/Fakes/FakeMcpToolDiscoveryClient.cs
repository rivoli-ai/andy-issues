// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Integration.Fakes;

public class FakeMcpToolDiscoveryClient : IMcpToolDiscoveryClient
{
    public McpToolDiscoveryResult Result { get; set; } =
        new(McpToolDiscoveryOutcome.Ok, Array.Empty<McpToolDescriptor>(), null);

    public List<(string url, string? headersJson)> Calls { get; } = new();

    public void Reset()
    {
        Calls.Clear();
        Result = new McpToolDiscoveryResult(McpToolDiscoveryOutcome.Ok, Array.Empty<McpToolDescriptor>(), null);
    }

    public Task<McpToolDiscoveryResult> DiscoverAsync(string url, string? headersJson, CancellationToken ct = default)
    {
        Calls.Add((url, headersJson));
        return Task.FromResult(Result);
    }
}
