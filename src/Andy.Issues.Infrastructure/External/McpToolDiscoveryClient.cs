// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.External;

/// <summary>
/// Implements the MCP JSON-RPC handshake over HTTP: POST <c>initialize</c>, then POST
/// <c>tools/list</c>, against the configured remote server. Errors are captured as
/// typed outcomes rather than exceptions so the controller can map to structured 502
/// responses; the only exception that escapes is <see cref="OperationCanceledException"/>
/// from a caller-cancelled request token.
/// </summary>
public class McpToolDiscoveryClient : IMcpToolDiscoveryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ILogger<McpToolDiscoveryClient> _logger;

    public McpToolDiscoveryClient(HttpClient http, ILogger<McpToolDiscoveryClient> logger)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(15);
        _logger = logger;
    }

    public async Task<McpToolDiscoveryResult> DiscoverAsync(
        string url,
        string? headersJson,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target))
            return Malformed($"Invalid MCP URL '{url}'.");

        Dictionary<string, string>? headers = null;
        if (!string.IsNullOrWhiteSpace(headersJson))
        {
            try
            {
                headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson, JsonOptions);
            }
            catch (JsonException)
            {
                return Malformed("headersJson is not a valid JSON object.");
            }
        }

        try
        {
            // Step 1: initialize
            var initialize = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "andy-issues", version = "1.0" }
                }
            };
            var initResponse = await PostJsonRpcAsync(target, headers, initialize, ct);
            if (!initResponse.IsSuccessStatusCode)
                return Http($"initialize returned {(int)initResponse.StatusCode}.");

            // Step 2: tools/list
            var listRequest = new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
                @params = new { }
            };
            using var listResponse = await PostJsonRpcAsync(target, headers, listRequest, ct);
            if (!listResponse.IsSuccessStatusCode)
                return Http($"tools/list returned {(int)listResponse.StatusCode}.");

            await using var stream = await listResponse.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
                return Malformed($"tools/list JSON-RPC error: {err.GetRawText()}");

            if (!root.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("tools", out var tools) ||
                tools.ValueKind != JsonValueKind.Array)
                return Malformed("tools/list response missing result.tools array.");

            var list = new List<McpToolDescriptor>(tools.GetArrayLength());
            foreach (var tool in tools.EnumerateArray())
            {
                var name = tool.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                    ? nameEl.GetString() ?? string.Empty
                    : string.Empty;
                if (string.IsNullOrEmpty(name)) continue;

                var description = tool.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String
                    ? descEl.GetString()
                    : null;

                JsonElement? schema = null;
                if (tool.TryGetProperty("inputSchema", out var schemaEl))
                    schema = schemaEl.Clone();

                list.Add(new McpToolDescriptor(name, description, schema));
            }

            return new McpToolDiscoveryResult(McpToolDiscoveryOutcome.Ok, list, null);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "MCP discovery timed out for {Url}.", url);
            return new McpToolDiscoveryResult(McpToolDiscoveryOutcome.Timeout, null, "MCP discovery timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "MCP discovery HTTP error for {Url}.", url);
            return Http(ex.Message);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "MCP discovery malformed response for {Url}.", url);
            return Malformed(ex.Message);
        }
    }

    private async Task<HttpResponseMessage> PostJsonRpcAsync(
        Uri target,
        Dictionary<string, string>? headers,
        object payload,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        if (headers is not null)
        {
            foreach (var (k, v) in headers)
                request.Headers.TryAddWithoutValidation(k, v);
        }
        return await _http.SendAsync(request, ct);
    }

    private static McpToolDiscoveryResult Http(string error) =>
        new(McpToolDiscoveryOutcome.HttpError, null, error);
    private static McpToolDiscoveryResult Malformed(string error) =>
        new(McpToolDiscoveryOutcome.MalformedResponse, null, error);
}
