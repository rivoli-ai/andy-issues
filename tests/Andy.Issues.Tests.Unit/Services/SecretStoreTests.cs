// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Infrastructure.External;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class SecretStoreTests
{
    // MARK: - ResolveAsync

    [Fact]
    public async Task Resolve_Null_ReturnsNull()
    {
        var store = CreateStore(new FakeSettingsClient());
        Assert.Null(await store.ResolveAsync(null));
    }

    [Fact]
    public async Task Resolve_Empty_ReturnsNull()
    {
        var store = CreateStore(new FakeSettingsClient());
        Assert.Null(await store.ResolveAsync(""));
    }

    [Fact]
    public async Task Resolve_RawValue_ReturnsSameValue()
    {
        var store = CreateStore(new FakeSettingsClient());
        Assert.Equal("plain-secret", await store.ResolveAsync("plain-secret"));
    }

    [Fact]
    public async Task Resolve_SecretRef_FetchesFromSettings()
    {
        var settings = new FakeSettingsClient();
        settings.Set("my.secret.key", "resolved-value");
        var store = CreateStore(settings);

        Assert.Equal("resolved-value", await store.ResolveAsync("secret::my.secret.key"));
    }

    [Fact]
    public async Task Resolve_MissingSecretRef_ReturnsNull()
    {
        var store = CreateStore(new FakeSettingsClient());
        Assert.Null(await store.ResolveAsync("secret::nonexistent"));
    }

    // MARK: - StoreAsync

    [Fact]
    public async Task Store_WithLocalSettings_ReturnsRawValue()
    {
        var config = new ConfigurationBuilder().Build();
        var localSettings = new LocalSettingsClient(config);
        var store = new SecretStore(localSettings, NullLogger<SecretStore>.Instance);

        var result = await store.StoreAsync("key", "my-secret");

        Assert.Equal("my-secret", result);
    }

    [Fact]
    public async Task Store_WithRealSettings_ReturnsSecretRef()
    {
        var settings = new FakeSettingsClient();
        var store = CreateStore(settings);

        var result = await store.StoreAsync("my.key", "value");

        Assert.Equal("secret::my.key", result);
    }

    // Helpers

    private static SecretStore CreateStore(IAndySettingsClient settings) =>
        new(settings, NullLogger<SecretStore>.Instance);

    private sealed class FakeSettingsClient : IAndySettingsClient
    {
        private readonly Dictionary<string, string> _store = new();

        public void Set(string key, string value) => _store[key] = value;

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            if (!_store.TryGetValue(key, out var raw))
                return Task.FromResult<T?>(default);

            if (typeof(T) == typeof(string))
                return Task.FromResult((T?)(object)raw);

            return Task.FromResult<T?>(default);
        }

        public Task<IReadOnlyDictionary<string, string>> GetBatchAsync(
            IEnumerable<string> keys, CancellationToken ct = default)
        {
            var result = new Dictionary<string, string>();
            foreach (var key in keys)
                if (_store.TryGetValue(key, out var value))
                    result[key] = value;
            return Task.FromResult<IReadOnlyDictionary<string, string>>(result);
        }
    }
}
