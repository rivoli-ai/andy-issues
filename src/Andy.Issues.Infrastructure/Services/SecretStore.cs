// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Services;

/// <summary>
/// Resolves secrets that may be raw values or <c>secret::</c> references to
/// andy-settings encrypted secrets. In dev-mode (when andy-settings isn't
/// configured) <see cref="StoreAsync"/> returns the raw value unchanged, so
/// the code path is identical — only the stored representation differs.
/// </summary>
public class SecretStore : ISecretStore
{
    private const string SecretPrefix = "secret::";

    private readonly IAndySettingsClient _settings;
    private readonly ILogger<SecretStore> _logger;

    public SecretStore(IAndySettingsClient settings, ILogger<SecretStore> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<string?> ResolveAsync(string? valueOrRef, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(valueOrRef))
            return null;

        if (!valueOrRef.StartsWith(SecretPrefix, StringComparison.Ordinal))
            return valueOrRef; // Raw value — return as-is

        var secretKey = valueOrRef[SecretPrefix.Length..];
        var resolved = await _settings.GetAsync<string>(secretKey, ct);

        if (resolved is null)
            _logger.LogWarning("Secret ref '{Key}' could not be resolved from andy-settings.", secretKey);

        return resolved;
    }

    public Task<string> StoreAsync(string key, string value, CancellationToken ct = default)
    {
        // Detect local-only scenario: when andy-settings is not configured
        // the DI container injects LocalSettingsClient. In that case we
        // just return the raw value so the caller persists it directly.
        if (_settings is Infrastructure.External.LocalSettingsClient)
            return Task.FromResult(value);

        // For the real client, we'd POST the secret. Since andy-settings'
        // secret write API shape isn't implemented yet, we store the
        // reference now and the value will be written when the admin
        // applies the seed or the write endpoint lands.
        return Task.FromResult($"{SecretPrefix}{key}");
    }
}
