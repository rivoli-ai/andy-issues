// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.External;

/// <summary>
/// HTTP-backed andy-settings client with per-request caching. The bearer is
/// minted by the OBO-aware <c>DelegatedBearerHandler</c> registered in
/// Program.cs (audience: <c>urn:andy-settings-api</c>) so user-scoped
/// settings resolve correctly. The cache is a plain dictionary — safe
/// because the Scoped DI lifetime guarantees one instance per HTTP request
/// (single-threaded within the request pipeline).
/// </summary>
public class AndySettingsClient : IAndySettingsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ILogger<AndySettingsClient> _logger;
    private readonly Dictionary<string, string?> _cache = new();

    public AndySettingsClient(
        IHttpClientFactory httpClientFactory,
        ILogger<AndySettingsClient> logger)
    {
        _http = httpClientFactory.CreateClient("AndySettings");
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var cached))
            return cached is null ? default : JsonSerializer.Deserialize<T>(cached, JsonOptions);

        try
        {
            var encoded = Uri.EscapeDataString(key);
            using var response = await _http.GetAsync($"api/settings/{encoded}", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _cache[key] = null;
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("andy-settings GET /api/settings/{Key} returned {Status}.", key, (int)response.StatusCode);
                return default;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var value = doc.RootElement.TryGetProperty("value", out var v)
                ? v.GetRawText()
                : null;

            _cache[key] = value;

            return value is null ? default : JsonSerializer.Deserialize<T>(value, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "andy-settings lookup failed for key '{Key}'.", key);
            return default;
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetBatchAsync(
        IEnumerable<string> keys,
        CancellationToken ct = default)
    {
        var keyList = keys.ToList();
        var result = new Dictionary<string, string>();

        // Serve what we can from cache
        var uncached = new List<string>();
        foreach (var key in keyList)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                if (cached is not null)
                    result[key] = cached;
            }
            else
            {
                uncached.Add(key);
            }
        }

        if (uncached.Count == 0)
            return result;

        try
        {
            var query = string.Join("&", uncached.Select(k => $"keys={Uri.EscapeDataString(k)}"));
            using var response = await _http.GetAsync($"api/settings/batch?{query}", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("andy-settings GET /api/settings/batch returned {Status}.", (int)response.StatusCode);
                return result;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var value = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.GetRawText();

                    _cache[prop.Name] = value;
                    if (value is not null)
                        result[prop.Name] = value;
                }
            }

            // Mark keys not in the response as absent
            foreach (var key in uncached)
            {
                if (!_cache.ContainsKey(key))
                    _cache[key] = null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "andy-settings batch lookup failed.");
        }

        return result;
    }
}
