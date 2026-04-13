// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Andy.Issues.Infrastructure.External;

/// <summary>
/// Dev-mode fallback when <c>AndySettings:ApiBaseUrl</c> is not configured.
/// Reads values from <see cref="IConfiguration"/> (appsettings.json / env vars)
/// using a dotted-key convention that maps andy-settings keys to config sections.
/// E.g. <c>andy.issues.containers.baseUrl</c> → <c>AndyContainers:BaseUrl</c>.
/// </summary>
public class LocalSettingsClient : IAndySettingsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfiguration _config;

    /// <summary>
    /// Maps andy-settings canonical keys to appsettings.json configuration paths.
    /// </summary>
    private static readonly Dictionary<string, string> KeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["andy.issues.azureDevops.defaultOrg"] = "AzureDevOps:DefaultOrganization",
        ["andy.issues.azureDevops.pullIntervalSeconds"] = "AzureDevOps:PullIntervalSeconds",
        ["andy.issues.codeIndex.baseUrl"] = "AndyCodeIndex:ApiBaseUrl",
        ["andy.issues.containers.baseUrl"] = "AndyContainers:BaseUrl",
        ["andy.issues.containers.templateCode"] = "AndyContainers:TemplateCode",
        ["andy.issues.containers.providerCode"] = "AndyContainers:ProviderCode",
        ["andy.issues.draftBacklog.defaultLlmSettingId"] = "DraftBacklog:DefaultLlmSettingId",
    };

    public LocalSettingsClient(IConfiguration config)
    {
        _config = config;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var raw = Resolve(key);
        if (raw is null)
            return Task.FromResult<T?>(default);

        // For string targets, return directly to avoid wrapping in extra quotes
        if (typeof(T) == typeof(string))
            return Task.FromResult((T?)(object)raw);

        try
        {
            // raw is a plain config string ("600", "true"), not JSON. Deserialize
            // it directly so numeric/boolean types parse correctly.
            var value = JsonSerializer.Deserialize<T>(raw, JsonOptions);
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
            var raw = Resolve(key);
            if (raw is not null)
                result[key] = raw;
        }
        return Task.FromResult<IReadOnlyDictionary<string, string>>(result);
    }

    private string? Resolve(string key)
    {
        if (KeyMap.TryGetValue(key, out var configPath))
            return _config[configPath];

        // Generic fallback: try the key as-is
        return _config[key];
    }
}
