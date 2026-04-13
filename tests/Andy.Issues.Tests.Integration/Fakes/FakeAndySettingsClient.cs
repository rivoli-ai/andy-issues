// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Integration.Fakes;

public class FakeAndySettingsClient : IAndySettingsClient
{
    private readonly Dictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string key, string value) => _store[key] = value;

    public void Reset() => _store.Clear();

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(key, out var raw))
            return Task.FromResult<T?>(default);

        if (typeof(T) == typeof(string))
            return Task.FromResult((T?)(object)raw);

        try
        {
            var value = System.Text.Json.JsonSerializer.Deserialize<T>(raw);
            return Task.FromResult(value);
        }
        catch
        {
            return Task.FromResult<T?>(default);
        }
    }

    public Task<IReadOnlyDictionary<string, string>> GetBatchAsync(
        IEnumerable<string> keys,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>();
        foreach (var key in keys)
        {
            if (_store.TryGetValue(key, out var value))
                result[key] = value;
        }
        return Task.FromResult<IReadOnlyDictionary<string, string>>(result);
    }
}
