// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Andy.Issues.Cli;

public sealed class ApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public ApiClient(string apiUrl, string? token)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/") };
        _ownsClient = true;
        if (!string.IsNullOrEmpty(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>Test constructor — caller owns the HttpClient lifetime.</summary>
    internal ApiClient(HttpClient httpClient)
    {
        _http = httpClient;
        _ownsClient = false;
    }

    public async Task<T?> GetAsync<T>(string path)
    {
        var response = await _http.GetAsync(path);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    public async Task<T?> PostAsync<T>(string path, object? body = null)
    {
        var response = await _http.PostAsJsonAsync(path, body, JsonOptions);
        await EnsureSuccessAsync(response);
        if (response.StatusCode == HttpStatusCode.NoContent)
            return default;
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    public async Task PostAsync(string path, object? body = null)
    {
        var response = await _http.PostAsJsonAsync(path, body, JsonOptions);
        await EnsureSuccessAsync(response);
    }

    public async Task<T?> PatchAsync<T>(string path, object body)
    {
        var content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.PatchAsync(path, content);
        await EnsureSuccessAsync(response);
        if (response.StatusCode == HttpStatusCode.NoContent)
            return default;
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    public async Task PatchAsync(string path, object body)
    {
        var content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.PatchAsync(path, content);
        await EnsureSuccessAsync(response);
    }

    public async Task DeleteAsync(string path)
    {
        var response = await _http.DeleteAsync(path);
        await EnsureSuccessAsync(response);
    }

    public static string ToJson<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();

        // Try to extract a structured error message
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    throw new CliException((int)response.StatusCode, err.GetString() ?? body);
                }
            }
            catch (JsonException) { }
        }

        throw new CliException((int)response.StatusCode,
            $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}" +
            (string.IsNullOrWhiteSpace(body) ? "" : $": {body}"));
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}

public sealed class CliException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
