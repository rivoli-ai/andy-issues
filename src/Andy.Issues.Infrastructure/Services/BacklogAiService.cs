// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Services;

/// <summary>
/// AI-assisted suggest-content service powering
/// <c>POST /api/backlog/suggest</c> (#87). Thin layer on top of the
/// same openai-chat-compatible HTTP contract <c>DraftBacklogGenerator</c>
/// uses — explicitly not extracted into a shared <c>ILlmClient</c>
/// abstraction yet, because the two call sites only share ~20 lines
/// of payload-building code and their prompt/response shapes diverge
/// (JSON object vs plain text).
/// </summary>
public class BacklogAiService : IBacklogAiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppDbContext _db;
    private readonly IRepositoryAccessGuard _guard;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BacklogAiService> _logger;

    public BacklogAiService(
        AppDbContext db,
        IRepositoryAccessGuard guard,
        IHttpClientFactory httpClientFactory,
        ILogger<BacklogAiService> logger)
    {
        _db = db;
        _guard = guard;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<(SuggestContentOutcome Outcome, string? Suggestion, string? Error)> SuggestContentAsync(
        SuggestContentRequest request,
        string userId,
        CancellationToken ct = default)
    {
        // Validate field + itemType before touching the DB so bad
        // inputs don't cost a round-trip.
        if (!TryParseField(request.Field, out var field))
            return (SuggestContentOutcome.InvalidField,
                null,
                $"Unknown field '{request.Field}'. Use 'description' or 'acceptanceCriteria'.");

        if (!TryParseItemType(request.ItemType, out var itemType))
            return (SuggestContentOutcome.InvalidItemType,
                null,
                $"Unknown itemType '{request.ItemType}'. Use 'epic', 'feature', or 'story'.");

        // Acceptance criteria only exist on stories in the schema.
        if (field == SuggestField.AcceptanceCriteria && itemType != BacklogItemType.Story)
            return (SuggestContentOutcome.InvalidField,
                null,
                "Acceptance criteria are only supported for stories.");

        // Resolve the LLM setting: prefer the repo's override, else
        // the caller's user-level default. Owner check applies only
        // when the caller supplied a repositoryId.
        LlmSetting? setting;
        if (request.RepositoryId is Guid repoId)
        {
            var repo = await _db.Repositories
                .Include(r => r.LlmSetting)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == repoId, ct);
            if (repo is null)
                return (SuggestContentOutcome.RepositoryNotFound, null, null);
            if (!await _guard.IsOwnerAsync(repoId, userId, ct))
                return (SuggestContentOutcome.NotOwner, null, null);
            setting = repo.LlmSetting
                ?? await DefaultLlmSettingAsync(userId, ct);
        }
        else
        {
            setting = await DefaultLlmSettingAsync(userId, ct);
        }

        if (setting is null)
            return (SuggestContentOutcome.NoLlmSetting,
                null,
                "No LLM setting is configured. Set one via POST /api/llm-settings, or link one to this repository.");

        var prompt = BuildPrompt(
            field: field,
            itemType: itemType,
            title: request.Title,
            currentContent: request.CurrentContent);

        string llmText;
        try
        {
            llmText = await CallLlmAsync(setting, prompt, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "LLM call failed for SuggestContent (field={Field} itemType={ItemType}).",
                request.Field, request.ItemType);
            return (SuggestContentOutcome.LlmCallFailed, null, ex.Message);
        }

        var cleaned = StripCodeFences(llmText).Trim();
        if (string.IsNullOrEmpty(cleaned))
            return (SuggestContentOutcome.ParseFailed,
                null,
                "LLM returned empty content.");

        return (SuggestContentOutcome.Ok, cleaned, null);
    }

    // MARK: - LLM call

    private const string SystemPrompt =
        "You are a senior product manager. Write clear, concrete backlog item drafts as plain prose. Do not include headings, code fences, or explanations — only the requested content.";

    public async Task<(bool Success, string Message)> TestConnectionAsync(
        LlmSetting setting,
        CancellationToken ct = default)
    {
        try
        {
            var reply = await CallLlmAsync(
                setting,
                prompt: "Reply with the single word OK.",
                ct);
            var trimmed = reply.Trim();
            if (trimmed.Length == 0)
                return (false, "Provider returned an empty response.");
            var preview = trimmed.Length <= 120
                ? trimmed
                : trimmed[..120] + "…";
            return (true, $"Connection OK. Model replied: \"{preview}\"");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "TestConnection HTTP failure for LlmSetting {SettingId}.",
                setting.Id);
            // ASP.NET's HttpRequestException carries the upstream
            // status code when available — surface it so the user
            // can tell 401 (bad key) from 429 (rate) from 5xx.
            var status = ex.StatusCode.HasValue
                ? $" (HTTP {(int)ex.StatusCode.Value} {ex.StatusCode.Value})"
                : string.Empty;
            return (false, $"Provider rejected the request{status}: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "TestConnection failure for LlmSetting {SettingId}.",
                setting.Id);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Routes the LLM call to the provider-specific endpoint and body
    /// shape. Anthropic's Messages API diverges enough from
    /// OpenAI's Chat Completions that a common path would either
    /// silently 404 or return a response we can't parse — which was
    /// the live symptom when a user configured an Anthropic key
    /// against a hard-coded <c>{baseUrl}/chat/completions</c> call.
    /// </summary>
    private Task<string> CallLlmAsync(
        LlmSetting setting,
        string prompt,
        CancellationToken ct)
    {
        return setting.Provider switch
        {
            LlmProvider.Anthropic => CallAnthropicAsync(setting, prompt, ct),
            // OpenAI, Ollama, and `Custom` all speak the OpenAI
            // Chat Completions wire format (the latter two by
            // convention — most self-hosted providers proxy it).
            _ => CallOpenAiChatAsync(setting, prompt, ct)
        };
    }

    private async Task<string> CallOpenAiChatAsync(
        LlmSetting setting,
        string prompt,
        CancellationToken ct)
    {
        var baseUrl = GetBaseUrl(setting);
        var client = _httpClientFactory.CreateClient("LlmProvider");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", setting.ApiKey);

        var payload = new
        {
            model = setting.Model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = prompt }
            },
            temperature = 0.7,
            max_tokens = 400
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
    private async Task<string> CallAnthropicAsync(
        LlmSetting setting,
        string prompt,
        CancellationToken ct)
    {
        var baseUrl = GetBaseUrl(setting);
        var client = _httpClientFactory.CreateClient("LlmProvider");
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
                    max_tokens = 400,
                    temperature = 0.7,
                    system = SystemPrompt,
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
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
        // order. A defensive fallback grabs the first block's `text`
        // field directly when the shape is unexpected.
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

    // MARK: - Prompt assembly

    /// <summary>
    /// Builds the user-role prompt for a given (field × itemType).
    /// Public + static so unit tests can pin the exact output —
    /// prompt drift is subtle and tests that only assert "a suggestion
    /// came back" miss wording regressions.
    /// </summary>
    public static string BuildPrompt(
        SuggestField field,
        BacklogItemType itemType,
        string title,
        string? currentContent)
    {
        var core = field switch
        {
            SuggestField.Description => itemType switch
            {
                BacklogItemType.Epic =>
                    $"Write a concise strategic goal for an epic titled \"{title}\". Focus on the high-level value it delivers and the problem it solves. 2–4 sentences.",
                BacklogItemType.Feature =>
                    $"Write a clear feature description for \"{title}\" explaining what the user can do once it ships. 2–4 sentences.",
                BacklogItemType.Story =>
                    $"Write a user-story description for \"{title}\" in the format \"As a [role], I want [capability], so that [benefit].\" Keep it to one or two sentences.",
                _ => throw new ArgumentOutOfRangeException(nameof(itemType))
            },
            SuggestField.AcceptanceCriteria =>
                $"Write acceptance criteria for the story \"{title}\" using Given/When/Then format. Produce three to five criteria, one per line, no numbering.",
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        if (string.IsNullOrWhiteSpace(currentContent))
            return core;

        // Refine-mode: the user has existing content. Anchor the LLM
        // to that draft rather than let it start from scratch.
        return $"""
            Refine the following draft so it better matches the request below. Preserve any specific details (names, versions, numeric thresholds) unless they're obviously placeholders.

            Existing draft:
            {currentContent}

            Request:
            {core}
            """;
    }

    /// <summary>
    /// Field enum exposed publicly so <c>BuildPrompt</c> tests can
    /// reference it without going through the string parser.
    /// </summary>
    public enum SuggestField
    {
        Description,
        AcceptanceCriteria
    }

    /// <summary>
    /// Mirror of Andy.Issues.Application types used by the suggest
    /// service. Declared here rather than importing so tests and the
    /// service share one authoritative set of cases.
    /// </summary>
    public enum BacklogItemType
    {
        Epic,
        Feature,
        Story
    }

    private static bool TryParseField(string raw, out SuggestField field)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "description":
                field = SuggestField.Description;
                return true;
            case "acceptancecriteria":
                field = SuggestField.AcceptanceCriteria;
                return true;
            default:
                field = default;
                return false;
        }
    }

    private static bool TryParseItemType(string raw, out BacklogItemType itemType)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "epic":
                itemType = BacklogItemType.Epic;
                return true;
            case "feature":
                itemType = BacklogItemType.Feature;
                return true;
            case "story":
                itemType = BacklogItemType.Story;
                return true;
            default:
                itemType = default;
                return false;
        }
    }

    // MARK: - Helpers

    /// <summary>
    /// Fetches the caller's default <see cref="LlmSetting"/> —
    /// the row with <see cref="LlmSetting.IsDefault"/> set for that
    /// user. Returns null when they have none, which the controller
    /// surfaces to the user as 400 NoLlmSetting.
    /// </summary>
    private Task<LlmSetting?> DefaultLlmSettingAsync(string userId, CancellationToken ct)
    {
        return _db.LlmSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerUserId == userId && s.IsDefault, ct);
    }

    /// <summary>
    /// Strips surrounding markdown code-fence lines that some models
    /// still emit despite being told not to. Defensive — the system
    /// prompt already forbids fences, but a rogue provider shouldn't
    /// put raw <c>```</c> into the user's editor.
    /// </summary>
    internal static string StripCodeFences(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var trimmed = input.Trim();
        if (!trimmed.StartsWith("```"))
            return trimmed;

        var lines = trimmed.Split('\n');
        if (lines.Length < 3)
            return trimmed;

        var start = 1; // skip opening fence
        var end = lines.Length;
        if (lines[^1].Trim() == "```")
            end--;

        return string.Join('\n', lines[start..end]);
    }
}
