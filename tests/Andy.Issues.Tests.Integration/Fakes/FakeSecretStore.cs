// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Integration.Fakes;

public class FakeSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = new();

    public void SetSecret(string key, string value) => _secrets[key] = value;
    public void Reset() => _secrets.Clear();

    public Task<string?> ResolveAsync(string? valueOrRef, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(valueOrRef))
            return Task.FromResult<string?>(null);

        if (valueOrRef.StartsWith("secret::", StringComparison.Ordinal))
        {
            var key = valueOrRef["secret::".Length..];
            return Task.FromResult<string?>(_secrets.GetValueOrDefault(key));
        }

        return Task.FromResult<string?>(valueOrRef);
    }

    public Task<string> StoreAsync(string key, string value, CancellationToken ct = default)
    {
        _secrets[key] = value;
        return Task.FromResult(value);
    }
}
