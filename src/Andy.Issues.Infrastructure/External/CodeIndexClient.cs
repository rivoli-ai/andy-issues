// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.External;

/// <summary>
/// HTTP client for the andy-code-index service. Registers repositories for
/// semantic indexing, polls their status, and deregisters them. Errors are
/// captured as typed outcomes — the only exception that escapes is
/// <see cref="OperationCanceledException"/> from a caller-cancelled token.
/// </summary>
public class CodeIndexClient : ICodeIndexClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ILogger<CodeIndexClient> _logger;

    public CodeIndexClient(HttpClient http, ILogger<CodeIndexClient> logger)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(30);
        _logger = logger;
    }

    public async Task<CodeIndexRegistrationResult> RegisterAsync(
        string cloneUrl,
        string defaultBranch,
        CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync("api/repositories", new
            {
                cloneUrl,
                defaultBranch
            }, JsonOptions, ct);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var indexId = await TryReadIndexIdAsync(response, ct);
                return new CodeIndexRegistrationResult(
                    CodeIndexRegistrationOutcome.AlreadyRegistered, indexId, null);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var error = await TryReadErrorAsync(response, ct);
                return new CodeIndexRegistrationResult(
                    CodeIndexRegistrationOutcome.InvalidUrl, null, error ?? "Invalid clone URL.");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "andy-code-index POST /api/repositories returned {Status} for {CloneUrl}.",
                    (int)response.StatusCode, cloneUrl);
                return new CodeIndexRegistrationResult(
                    CodeIndexRegistrationOutcome.ServiceUnavailable, null,
                    $"Unexpected status {(int)response.StatusCode}.");
            }

            var id = await TryReadIndexIdAsync(response, ct);
            return new CodeIndexRegistrationResult(
                CodeIndexRegistrationOutcome.Registered, id, null);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "andy-code-index registration timed out for {CloneUrl}.", cloneUrl);
            return new CodeIndexRegistrationResult(
                CodeIndexRegistrationOutcome.ServiceUnavailable, null, "Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "andy-code-index registration failed for {CloneUrl}.", cloneUrl);
            return new CodeIndexRegistrationResult(
                CodeIndexRegistrationOutcome.ServiceUnavailable, null, ex.Message);
        }
    }

    public async Task<CodeIndexStatusResult> GetStatusAsync(
        string cloneUrl,
        CancellationToken ct = default)
    {
        try
        {
            var encoded = Uri.EscapeDataString(cloneUrl);
            using var response = await _http.GetAsync($"api/repositories/status?cloneUrl={encoded}", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new CodeIndexStatusResult(CodeIndexQueryOutcome.NotFound, null, null, null);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "andy-code-index GET /api/repositories/status returned {Status} for {CloneUrl}.",
                    (int)response.StatusCode, cloneUrl);
                return new CodeIndexStatusResult(
                    CodeIndexQueryOutcome.ServiceUnavailable, null, null,
                    $"Unexpected status {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var status = root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString() : null;
            var summary = root.TryGetProperty("summary", out var sum) && sum.ValueKind == JsonValueKind.String
                ? sum.GetString() : null;

            return new CodeIndexStatusResult(CodeIndexQueryOutcome.Ok, status, summary, null);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "andy-code-index status check timed out for {CloneUrl}.", cloneUrl);
            return new CodeIndexStatusResult(
                CodeIndexQueryOutcome.ServiceUnavailable, null, null, "Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "andy-code-index status check failed for {CloneUrl}.", cloneUrl);
            return new CodeIndexStatusResult(
                CodeIndexQueryOutcome.ServiceUnavailable, null, null, ex.Message);
        }
    }

    public async Task<bool> DeregisterAsync(
        string cloneUrl,
        CancellationToken ct = default)
    {
        try
        {
            var encoded = Uri.EscapeDataString(cloneUrl);
            using var response = await _http.DeleteAsync($"api/repositories?cloneUrl={encoded}", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return false;

            return response.IsSuccessStatusCode;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "andy-code-index deregister timed out for {CloneUrl}.", cloneUrl);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "andy-code-index deregister failed for {CloneUrl}.", cloneUrl);
            return false;
        }
    }

    private static async Task<string?> TryReadIndexIdAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("indexId", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
