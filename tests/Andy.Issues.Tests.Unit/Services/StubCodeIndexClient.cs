// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Unit.Services;

public class StubCodeIndexClient : ICodeIndexClient
{
    public CodeIndexRegistrationResult RegistrationResult { get; set; } =
        new(CodeIndexRegistrationOutcome.Registered, "idx-stub", null);

    public List<(string cloneUrl, string defaultBranch)> RegisterCalls { get; } = new();

    public Task<CodeIndexRegistrationResult> RegisterAsync(
        string cloneUrl, string defaultBranch, CancellationToken ct = default)
    {
        RegisterCalls.Add((cloneUrl, defaultBranch));
        return Task.FromResult(RegistrationResult);
    }

    public Task<CodeIndexStatusResult> GetStatusAsync(
        string cloneUrl, CancellationToken ct = default) =>
        Task.FromResult(new CodeIndexStatusResult(CodeIndexQueryOutcome.Ok, "Indexed", null, null));

    public Task<bool> DeregisterAsync(
        string cloneUrl, CancellationToken ct = default) =>
        Task.FromResult(true);
}
