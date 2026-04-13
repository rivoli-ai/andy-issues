// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Services;

public class DraftBacklogGenerator : IDraftBacklogGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppDbContext _db;
    private readonly IRepositoryAccessGuard _guard;
    private readonly ICodeIndexClient _codeIndex;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DraftBacklogGenerator> _logger;

    public DraftBacklogGenerator(
        AppDbContext db,
        IRepositoryAccessGuard guard,
        ICodeIndexClient codeIndex,
        IHttpClientFactory httpClientFactory,
        ILogger<DraftBacklogGenerator> logger)
    {
        _db = db;
        _guard = guard;
        _codeIndex = codeIndex;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<DraftBacklogResult> GenerateAsync(
        Guid repositoryId,
        string userId,
        CancellationToken ct = default)
    {
        var repo = await _db.Repositories
            .Include(r => r.LlmSetting)
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct);

        if (repo is null)
            return new DraftBacklogResult(DraftBacklogOutcome.RepositoryNotFound, null, null);

        if (!await _guard.IsOwnerAsync(repositoryId, userId, ct))
            return new DraftBacklogResult(DraftBacklogOutcome.NotOwner, null, null);

        if (repo.LlmSetting is null)
            return new DraftBacklogResult(DraftBacklogOutcome.NoLlmSetting, null,
                "Repository has no LLM setting configured. Link one via PATCH /api/repositories/{id}/llm-setting.");

        // Fetch code summary from andy-code-index
        var indexStatus = await _codeIndex.GetStatusAsync(repo.CloneUrl, ct);
        if (indexStatus.Outcome != CodeIndexQueryOutcome.Ok || string.IsNullOrEmpty(indexStatus.Summary))
            return new DraftBacklogResult(DraftBacklogOutcome.CodeIndexNotReady, null,
                indexStatus.Error ?? "Code index has no summary available yet.");

        // Call LLM
        string llmResponse;
        try
        {
            llmResponse = await CallLlmAsync(repo.LlmSetting, repo.Name, indexStatus.Summary, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "LLM call failed for repository {RepositoryId}.", repositoryId);
            return new DraftBacklogResult(DraftBacklogOutcome.LlmCallFailed, null, ex.Message);
        }

        // Parse structured backlog from LLM response
        List<DraftEpic> draft;
        try
        {
            draft = ParseDraftBacklog(llmResponse);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM backlog response for repository {RepositoryId}.", repositoryId);
            return new DraftBacklogResult(DraftBacklogOutcome.ParseFailed, null,
                $"Could not parse LLM response: {ex.Message}");
        }

        // Persist the draft items
        var epicOrder = await NextEpicOrderAsync(repositoryId, ct);
        foreach (var de in draft)
        {
            var epic = new Epic
            {
                Id = Guid.NewGuid(),
                RepositoryId = repositoryId,
                Title = de.Title,
                Description = de.Description,
                Order = epicOrder++
            };

            var featureOrder = 1;
            foreach (var df in de.Features)
            {
                var feature = new Feature
                {
                    Id = Guid.NewGuid(),
                    EpicId = epic.Id,
                    Title = df.Title,
                    Description = df.Description,
                    Order = featureOrder++
                };

                var storyOrder = 1;
                foreach (var ds in df.Stories)
                {
                    feature.Stories.Add(new UserStory
                    {
                        Id = Guid.NewGuid(),
                        FeatureId = feature.Id,
                        Title = ds.Title,
                        Description = ds.Description,
                        AcceptanceCriteria = ds.AcceptanceCriteria,
                        StoryPoints = ds.StoryPoints,
                        Order = storyOrder++
                    });
                }

                epic.Features.Add(feature);
            }

            _db.Epics.Add(epic);
        }

        await _db.SaveChangesAsync(ct);

        // Reload the full backlog for the response DTO
        var reloaded = await _db.Repositories
            .AsNoTracking()
            .Include(r => r.Epics).ThenInclude(e => e.Features).ThenInclude(f => f.Stories)
            .FirstAsync(r => r.Id == repositoryId, ct);

        return new DraftBacklogResult(
            DraftBacklogOutcome.Generated,
            reloaded.ToBacklogDto(),
            null);
    }

    private async Task<string> CallLlmAsync(
        LlmSetting setting,
        string repoName,
        string codeSummary,
        CancellationToken ct)
    {
        var baseUrl = GetBaseUrl(setting);
        var client = _httpClientFactory.CreateClient("LlmProvider");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", setting.ApiKey);

        var prompt = BuildPrompt(repoName, codeSummary);
        var payload = new
        {
            model = setting.Model,
            messages = new[]
            {
                new { role = "system", content = "You are a software architect. Generate structured product backlogs in JSON." },
                new { role = "user", content = prompt }
            },
            temperature = 0.3,
            response_format = new { type = "json_object" }
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

        return content ?? throw new InvalidOperationException("LLM returned empty content.");
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

    private static string BuildPrompt(string repoName, string codeSummary)
    {
        return $$"""
            Analyze the following code summary for the repository "{{repoName}}" and generate a
            product backlog as a JSON object with the structure below. Focus on practical development
            tasks, improvements, and features that would make sense given the existing codebase.

            Code summary:
            {{codeSummary}}

            Return ONLY a JSON object with this structure:
            {
              "epics": [
                {
                  "title": "Epic title",
                  "description": "Epic description",
                  "features": [
                    {
                      "title": "Feature title",
                      "description": "Feature description",
                      "stories": [
                        {
                          "title": "User story title",
                          "description": "As a <role>, I want <goal> so that <benefit>",
                          "acceptanceCriteria": "Given/When/Then criteria",
                          "storyPoints": 3
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """;
    }

    public static List<DraftEpic> ParseDraftBacklog(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("epics", out var epicsEl) || epicsEl.ValueKind != JsonValueKind.Array)
            throw new JsonException("Response missing 'epics' array.");

        var result = new List<DraftEpic>();
        foreach (var epicEl in epicsEl.EnumerateArray())
        {
            var title = epicEl.GetProperty("title").GetString() ?? "Untitled Epic";
            var desc = epicEl.TryGetProperty("description", out var d) ? d.GetString() : null;

            var features = new List<DraftFeature>();
            if (epicEl.TryGetProperty("features", out var featuresEl) && featuresEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var featureEl in featuresEl.EnumerateArray())
                {
                    var fTitle = featureEl.GetProperty("title").GetString() ?? "Untitled Feature";
                    var fDesc = featureEl.TryGetProperty("description", out var fd) ? fd.GetString() : null;

                    var stories = new List<DraftStory>();
                    if (featureEl.TryGetProperty("stories", out var storiesEl) && storiesEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var storyEl in storiesEl.EnumerateArray())
                        {
                            var sTitle = storyEl.GetProperty("title").GetString() ?? "Untitled Story";
                            var sDesc = storyEl.TryGetProperty("description", out var sd) ? sd.GetString() : null;
                            var sCriteria = storyEl.TryGetProperty("acceptanceCriteria", out var sc) ? sc.GetString() : null;
                            int? sPoints = storyEl.TryGetProperty("storyPoints", out var sp)
                                && sp.ValueKind == JsonValueKind.Number
                                ? sp.GetInt32() : null;
                            stories.Add(new DraftStory(sTitle, sDesc, sCriteria, sPoints));
                        }
                    }

                    features.Add(new DraftFeature(fTitle, fDesc, stories));
                }
            }

            result.Add(new DraftEpic(title, desc, features));
        }

        return result;
    }

    private async Task<int> NextEpicOrderAsync(Guid repositoryId, CancellationToken ct)
    {
        var max = await _db.Epics
            .AsNoTracking()
            .Where(e => e.RepositoryId == repositoryId)
            .Select(e => (int?)e.Order)
            .MaxAsync(ct) ?? 0;
        return max + 1;
    }

    public record DraftEpic(string Title, string? Description, List<DraftFeature> Features);
    public record DraftFeature(string Title, string? Description, List<DraftStory> Stories);
    public record DraftStory(string Title, string? Description, string? AcceptanceCriteria, int? StoryPoints);
}
