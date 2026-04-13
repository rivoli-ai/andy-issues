// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Integration.Fakes;

public class FakeCodeIndexClient : ICodeIndexClient
{
    public CodeIndexRegistrationResult RegistrationResult { get; set; } =
        new(CodeIndexRegistrationOutcome.Registered, "idx-001", null);

    public CodeIndexStatusResult StatusResult { get; set; } =
        new(CodeIndexQueryOutcome.Ok, "Indexed", "Repository summary", null);

    public bool DeregisterResult { get; set; } = true;

    public List<(string cloneUrl, string defaultBranch)> RegisterCalls { get; } = new();
    public List<string> StatusCalls { get; } = new();
    public List<string> DeregisterCalls { get; } = new();

    public void Reset()
    {
        RegisterCalls.Clear();
        StatusCalls.Clear();
        DeregisterCalls.Clear();
        RegistrationResult = new(CodeIndexRegistrationOutcome.Registered, "idx-001", null);
        StatusResult = new(CodeIndexQueryOutcome.Ok, "Indexed", "Repository summary", null);
        DeregisterResult = true;
    }

    public Task<CodeIndexRegistrationResult> RegisterAsync(
        string cloneUrl, string defaultBranch, CancellationToken ct = default)
    {
        RegisterCalls.Add((cloneUrl, defaultBranch));
        return Task.FromResult(RegistrationResult);
    }

    public Task<CodeIndexStatusResult> GetStatusAsync(
        string cloneUrl, CancellationToken ct = default)
    {
        StatusCalls.Add(cloneUrl);
        return Task.FromResult(StatusResult);
    }

    public Task<bool> DeregisterAsync(
        string cloneUrl, CancellationToken ct = default)
    {
        DeregisterCalls.Add(cloneUrl);
        return Task.FromResult(DeregisterResult);
    }
}
