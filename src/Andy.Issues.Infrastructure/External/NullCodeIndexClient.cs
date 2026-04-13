// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Infrastructure.External;

/// <summary>
/// No-op implementation used when <c>AndyCodeIndex:ApiBaseUrl</c> is not configured.
/// All operations return "service unavailable" so callers degrade gracefully.
/// </summary>
public class NullCodeIndexClient : ICodeIndexClient
{
    public Task<CodeIndexRegistrationResult> RegisterAsync(
        string cloneUrl, string defaultBranch, CancellationToken ct = default) =>
        Task.FromResult(new CodeIndexRegistrationResult(
            CodeIndexRegistrationOutcome.ServiceUnavailable, null, "andy-code-index is not configured."));

    public Task<CodeIndexStatusResult> GetStatusAsync(
        string cloneUrl, CancellationToken ct = default) =>
        Task.FromResult(new CodeIndexStatusResult(
            CodeIndexQueryOutcome.ServiceUnavailable, null, null, "andy-code-index is not configured."));

    public Task<bool> DeregisterAsync(
        string cloneUrl, CancellationToken ct = default) =>
        Task.FromResult(false);
}
