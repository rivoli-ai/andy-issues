// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Andy.Issues.Infrastructure.External;

// Wave 5 implementation: reads the triage agent id from configuration
// (`Triage:AgentId`). Returns null when unset so callers (#111
// StartTriageAsync) can degrade gracefully — the issue still
// transitions to `Triaging`; a human can re-invoke once the agent is
// configured. The dynamic-discovery variant (HTTP to andy-agents) is
// planned for after Epic W lands.
public class ConfigAgentsClient : IAgentsClient
{
    private readonly IConfiguration _config;

    public ConfigAgentsClient(IConfiguration config)
    {
        _config = config;
    }

    public Task<AgentDescriptor?> GetTriageAgentAsync(CancellationToken ct = default)
    {
        var id = _config["Triage:AgentId"];
        var version = _config["Triage:AgentVersion"];
        if (string.IsNullOrWhiteSpace(id))
            return Task.FromResult<AgentDescriptor?>(null);
        return Task.FromResult<AgentDescriptor?>(
            new AgentDescriptor(id, string.IsNullOrWhiteSpace(version) ? null : version));
    }
}
