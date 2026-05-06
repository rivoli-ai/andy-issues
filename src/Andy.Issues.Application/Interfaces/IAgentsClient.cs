// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Interfaces;

public sealed record AgentDescriptor(string AgentId, string? Version = null);

// Thin abstraction over andy-agents. Wave 5 ships with a config-backed
// implementation that returns whatever is in `Triage:AgentId`; later
// (once Epic W lands) a real HTTP adapter will hit
// `GET /agents/api/agents?role=triage` against andy-agents and pick
// the right per-tenant agent dynamically.
public interface IAgentsClient
{
    Task<AgentDescriptor?> GetTriageAgentAsync(CancellationToken ct = default);
}
