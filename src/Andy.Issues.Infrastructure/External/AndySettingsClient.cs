// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
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
    private readonly string? _userId;
    private readonly ILogger<AndySettingsClient> _logger;
    private readonly Dictionary<string, string?> _cache = new();

    public AndySettingsClient(
        IHttpClientFactory httpClientFactory,
        ILogger<AndySettingsClient> logger,
        string? userId = null)
    {
        _http = httpClientFactory.CreateClient("AndySettings");
        _logger = logger;
        _userId = userId;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var cached))
            return cached is null ? default : JsonSerializer.Deserialize<T>(cached, JsonOptions);

        try
        {
            using var response = await _http.PostAsJsonAsync("api/effective/resolve",
                new { key, context = ResolutionContext() }, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _cache[key] = null;
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("andy-settings resolve {Key} returned {Status}.", key, (int)response.StatusCode);
                return default;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var value = ReadEffectiveValue(doc.RootElement);

            _cache[key] = value;

            return value is null ? default : JsonSerializer.Deserialize<T>(value, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "andy-settings lookup failed for key '{Key}'.", key);
            return default;
        }
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var encoded = Uri.EscapeDataString(key);
            using var response = await _http.GetAsync($"api/secrets/{encoded}?scopeType=Machine", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("andy-settings secret lookup for {Key} returned {Status}.", key, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Do not log response bodies or exception details from secret lookups.
            _logger.LogWarning("andy-settings secret lookup failed for {Key} ({ErrorType}).", key, ex.GetType().Name);
            return null;
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
                    result[key] = BatchValue(cached);
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
            using var response = await _http.PostAsJsonAsync("api/effective/resolve-batch",
                new { keys = uncached, context = ResolutionContext() }, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("andy-settings resolve-batch returned {Status}.", (int)response.StatusCode);
                return result;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var key = entry.GetProperty("key").GetString();
                if (key is null || !uncached.Contains(key)) continue;
                var value = ReadEffectiveValue(entry);
                _cache[key] = value;
                if (value is not null) result[key] = BatchValue(value);
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
    private object ResolutionContext() => new
    {
        applicationCode = "andy-issues",
        userId = _userId
    };

    private static string? ReadEffectiveValue(JsonElement entry)
    {
        if (entry.TryGetProperty("isSecret", out var secret) && secret.ValueKind == JsonValueKind.True)
            return null;
        if (entry.TryGetProperty("isValid", out var valid) && valid.ValueKind == JsonValueKind.False)
            return null;
        return entry.TryGetProperty("effectiveValue", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
    }

    private static string BatchValue(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString()! : json;
    }

}
