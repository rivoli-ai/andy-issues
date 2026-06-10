// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Infrastructure.Services;

/// <summary>
/// Provider-aware single-turn LLM chat completion shared by
/// <see cref="BacklogAiService"/> and
/// <see cref="BacklogRecategorizeService"/>.
///
/// Anthropic's Messages API (<c>POST {base}/messages</c> with
/// <c>x-api-key</c> + <c>anthropic-version</c> headers and a dedicated
/// top-level <c>system</c> field) diverges enough from OpenAI's Chat
/// Completions (<c>POST {base}/chat/completions</c> with Bearer auth)
/// that a common code path would silently 404 or return an unparseable
/// body — the live symptom that motivated the original split in
/// <c>BacklogAiService</c>. OpenAI, Ollama, and <c>Custom</c> all speak
/// the OpenAI wire format (the latter two by convention — most
/// self-hosted providers proxy it).
/// </summary>
internal static class LlmChatCompletion
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Runs one system+user turn against the provider configured on
    /// <paramref name="setting"/> and returns the assistant's text.
    /// </summary>
    /// <param name="requestJsonObject">
    /// When true, the OpenAI-compatible path adds
    /// <c>response_format: {type: "json_object"}</c> (matching
    /// <c>DraftBacklogGenerator</c>'s JSON-mode call). Anthropic has no
    /// equivalent switch — the prompt itself must demand strict JSON.
    /// </param>
    internal static Task<string> CompleteAsync(
        IHttpClientFactory httpClientFactory,
        LlmSetting setting,
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        double temperature,
        bool requestJsonObject,
        CancellationToken ct)
    {
        return setting.Provider switch
        {
            LlmProvider.Anthropic => CallAnthropicAsync(
                httpClientFactory, setting, systemPrompt, userPrompt, maxTokens, temperature, ct),
            _ => CallOpenAiChatAsync(
                httpClientFactory, setting, systemPrompt, userPrompt,
                maxTokens, temperature, requestJsonObject, ct)
        };
    }

    private static async Task<string> CallOpenAiChatAsync(
        IHttpClientFactory httpClientFactory,
        LlmSetting setting,
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        double temperature,
        bool requestJsonObject,
        CancellationToken ct)
    {
        var baseUrl = GetBaseUrl(setting);
        var client = httpClientFactory.CreateClient("LlmProvider");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", setting.ApiKey);

        object payload = requestJsonObject
            ? new
            {
                model = setting.Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature,
                max_tokens = maxTokens,
                response_format = new { type = "json_object" }
            }
            : new
            {
                model = setting.Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature,
                max_tokens = maxTokens
            };

        using var response = await client.PostAsJsonAsync(
            $"{baseUrl}/chat/completions", payload, JsonOptions, ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content ?? throw new InvalidOperationException("LLM returned null content.");
    }

    /// <summary>
    /// Anthropic's Messages API: <c>POST /v1/messages</c> with
    /// <c>x-api-key</c> + <c>anthropic-version</c> headers, system
    /// prompt in a dedicated top-level field (not a role), and a
    /// response body shaped as <c>content: [{type: "text",
    /// text: "…"}]</c>.
    /// </summary>
    private static async Task<string> CallAnthropicAsync(
        IHttpClientFactory httpClientFactory,
        LlmSetting setting,
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        double temperature,
        CancellationToken ct)
    {
        var baseUrl = GetBaseUrl(setting);
        var client = httpClientFactory.CreateClient("LlmProvider");
        // Anthropic uses `x-api-key` instead of Bearer. Clear any
        // Authorization header the factory may have left on the
        // pooled client from a previous call.
        client.DefaultRequestHeaders.Authorization = null;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/messages")
        {
            Content = JsonContent.Create(
                new
                {
                    model = setting.Model,
                    max_tokens = maxTokens,
                    temperature,
                    system = systemPrompt,
                    messages = new[]
                    {
                        new { role = "user", content = userPrompt }
                    }
                },
                options: JsonOptions)
        };
        request.Headers.Add("x-api-key", setting.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        // The `content` array can mix text and tool-use blocks; we
        // only care about text blocks and we concatenate them in
        // order.
        if (doc.RootElement.TryGetProperty("content", out var blocks)
            && blocks.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var block in blocks.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type)
                    && type.GetString() == "text"
                    && block.TryGetProperty("text", out var text)
                    && text.GetString() is { } chunk)
                {
                    parts.Add(chunk);
                }
            }
            if (parts.Count > 0)
                return string.Join("\n\n", parts);
        }

        throw new InvalidOperationException("Anthropic response did not contain any text content blocks.");
    }

    private static string GetBaseUrl(LlmSetting setting)
    {
        if (!string.IsNullOrEmpty(setting.BaseUrl))
            return setting.BaseUrl.TrimEnd('/');

        return setting.Provider switch
        {
            LlmProvider.OpenAI => "https://api.openai.com/v1",
            LlmProvider.Anthropic => "https://api.anthropic.com/v1",
            LlmProvider.Ollama => "http://localhost:11434/v1",
            _ => throw new InvalidOperationException($"No base URL configured for provider {setting.Provider}.")
        };
    }
}
