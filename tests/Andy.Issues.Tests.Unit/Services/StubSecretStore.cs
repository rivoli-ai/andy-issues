// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Unit.Services;

/// <summary>
/// Pass-through secret store for unit tests: Resolve returns the raw value,
/// Store returns the raw value unchanged (no andy-settings indirection).
/// </summary>
public class StubSecretStore : ISecretStore
{
    public Task<string?> ResolveAsync(string? valueOrRef, CancellationToken ct = default) =>
        Task.FromResult(valueOrRef);

    public Task<string> StoreAsync(string key, string value, CancellationToken ct = default) =>
        Task.FromResult(value);
}
