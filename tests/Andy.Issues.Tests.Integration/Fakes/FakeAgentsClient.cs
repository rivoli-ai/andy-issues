// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Integration.Fakes;

public class FakeAgentsClient : IAgentsClient
{
    public AgentDescriptor? TriageAgent { get; set; }
    public int CallCount { get; private set; }

    public void Reset()
    {
        TriageAgent = null;
        CallCount = 0;
    }

    public Task<AgentDescriptor?> GetTriageAgentAsync(CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(TriageAgent);
    }
}
