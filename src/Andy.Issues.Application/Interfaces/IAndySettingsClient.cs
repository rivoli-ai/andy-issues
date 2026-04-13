// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Interfaces;

/// <summary>
/// Reads typed configuration values from the andy-settings service. Values
/// are cached for the lifetime of the request scope (Scoped DI lifetime)
/// so repeated reads inside a single HTTP request pay at most one outbound
/// call per key. When <c>AndySettings:ApiBaseUrl</c> is empty the
/// implementation falls back to <c>IConfiguration</c> for dev-mode.
/// </summary>
public interface IAndySettingsClient
{
    /// <summary>
    /// Gets a single setting by key, deserialized to <typeparamref name="T"/>.
    /// Returns <c>default</c> if the key does not exist.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Gets multiple settings in a single round-trip. Keys that don't exist
    /// are omitted from the result dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetBatchAsync(
        IEnumerable<string> keys,
        CancellationToken ct = default);
}
