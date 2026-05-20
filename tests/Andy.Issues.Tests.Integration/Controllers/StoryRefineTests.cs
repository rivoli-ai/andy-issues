// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

// SP.0.4 (andy-issues#180 / conductor#1632) — end-to-end verification
// of POST /api/stories/{id}/refine through the full stack:
//
//   1. Caller POSTs the refine request.
//   2. Endpoint returns 202 with the run id + version.
//   3. Background task runs the EchoStoryTriageAgent stub and writes
//      the classification + refined description / acceptance criteria
//      / risks / test plan back to the story row.
//   4. GET /api/stories/{id} returns the full UserStoryDto with the
//      Triaged state shape Conductor's RefinementPanel expects.
//   5. An `andy.issues.events.story.{id}.triaged` outbox row is queued
//      for the NATS dispatcher.
public class StoryRefineTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            // Wire enum values are lowercase ("p0", "trivial", "low"),
            // matching the JsonStringEnumConverter the controller uses.
            new System.Text.Json.Serialization.JsonStringEnumConverter()
        }
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public StoryRefineTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<Guid> SeedStoryAsync(string ownerUserId, IEnumerable<string>? labels = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var allocator = scope.ServiceProvider.GetRequiredService<IBacklogSequenceAllocator>();

        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Name = "refine-repo",
            CloneUrl = $"https://example.com/{Guid.NewGuid():N}.git"
        };
        db.Repositories.Add(repo);

        var epic = new Epic { Id = Guid.NewGuid(), RepositoryId = repo.Id, Title = "E", Order = 1 };
        db.Epics.Add(epic);

        var feature = new Feature { Id = Guid.NewGuid(), EpicId = epic.Id, Title = "F", Order = 1 };
        db.Features.Add(feature);

        var story = new UserStory
        {
            Id = Guid.NewGuid(),
            Seq = await allocator.AllocateAsync(Andy.Issues.Domain.Enums.BacklogEntityType.Story),
            FeatureId = feature.Id,
            Title = "Refine me",
            Description = "Original body",
            Labels = labels?.ToList() ?? new List<string>(),
            Order = 1
        };
        db.UserStories.Add(story);
        await db.SaveChangesAsync();
        return story.Id;
    }

    private async Task<UserStoryDto?> GetStoryAsync(Guid storyId)
    {
        var resp = await _client.GetAsync($"/api/stories/{storyId}");
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<UserStoryDto>(JsonOptions);
    }

    private async Task<UserStoryDto?> WaitForTriagedAsync(Guid storyId, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var dto = await GetStoryAsync(storyId);
            if (dto?.Refinement is not null) return dto;
            await Task.Delay(50);
        }
        return null;
    }

    [Fact]
    public async Task Refine_UnknownStory_Returns404()
    {
        var resp = await _client.PostAsJsonAsync(
            $"/api/stories/{Guid.NewGuid()}/refine",
            new { instructions = (string?)null, agentId = (string?)null });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Refine_HappyPath_Returns202_AndRunId()
    {
        var storyId = await SeedStoryAsync("dev-user");

        var resp = await _client.PostAsJsonAsync(
            $"/api/stories/{storyId}/refine",
            new { instructions = "ship safely", agentId = "echo" });

        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<StoryRefineRunDto>(JsonOptions);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.RefineRunId);
        Assert.Equal(1, body.RefineVersion);
    }

    [Fact]
    public async Task Refine_EmptyBody_AllowedAndQueued()
    {
        var storyId = await SeedStoryAsync("dev-user");

        // Empty body — both Instructions and AgentId default to null.
        var resp = await _client.PostAsync(
            $"/api/stories/{storyId}/refine",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
    }

    [Fact]
    public async Task Refine_BackgroundTask_PersistsRefinementAndTriageState()
    {
        var storyId = await SeedStoryAsync("dev-user", labels: new[] { "p1" });

        var resp = await _client.PostAsJsonAsync(
            $"/api/stories/{storyId}/refine", new RefineRequest());
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);

        var dto = await WaitForTriagedAsync(storyId);
        Assert.NotNull(dto);

        Assert.NotNull(dto!.TriageState);
        Assert.IsType<StoryTriageStateDto.Triaged>(dto.TriageState);

        Assert.NotNull(dto.Refinement);
        Assert.Equal(1, dto.Refinement!.RefineVersion);
        Assert.NotEmpty(dto.Refinement.AcceptanceCriteria);
        Assert.NotEmpty(dto.Refinement.Risks);
        Assert.NotEmpty(dto.Refinement.TestPlan);
        Assert.Equal(StoryPriorityWire.p1, dto.Refinement.Classification.Priority);
        Assert.Equal(StoryRiskWire.medium, dto.Refinement.Classification.Risk);
    }

    [Fact]
    public async Task Refine_EmitsTriagedOutboxRow_WithExpectedShape()
    {
        var storyId = await SeedStoryAsync("dev-user");

        await _client.PostAsJsonAsync($"/api/stories/{storyId}/refine", new RefineRequest());
        await WaitForTriagedAsync(storyId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.Outbox.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Subject == $"andy.issues.events.story.{storyId}.triaged");

        Assert.NotNull(entry);
        Assert.Equal(storyId, entry!.CorrelationId);

        using var doc = JsonDocument.Parse(entry.PayloadJson);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("story_id", out _));
        Assert.True(root.TryGetProperty("refine_version", out var v));
        Assert.Equal(1, v.GetInt32());
        Assert.True(root.TryGetProperty("refined_by", out _));
        Assert.True(root.TryGetProperty("refined_at", out _));

        Assert.True(root.TryGetProperty("classification", out var cls));
        Assert.True(cls.TryGetProperty("priority", out _));
        Assert.True(cls.TryGetProperty("complexity", out _));
        Assert.True(cls.TryGetProperty("risk", out _));
        Assert.True(cls.TryGetProperty("suggested_approach", out _));

        Assert.True(root.TryGetProperty("triage_state", out var ts));
        Assert.Equal("Triaged", ts.GetProperty("kind").GetString());
        Assert.True(ts.TryGetProperty("version", out _));
        Assert.True(ts.TryGetProperty("at", out _));
    }

    [Fact]
    public async Task Refine_NotOwnerOfStory_Returns404()
    {
        var storyId = await SeedStoryAsync("other-user");

        var resp = await _client.PostAsJsonAsync(
            $"/api/stories/{storyId}/refine", new RefineRequest());

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── Contract: response shape matches Conductor's StoryRefinementDTO ─

    [Fact]
    public async Task Refine_StoryDto_MatchesConductorRefinementWireContract()
    {
        var storyId = await SeedStoryAsync("dev-user");
        await _client.PostAsJsonAsync($"/api/stories/{storyId}/refine", new RefineRequest());
        await WaitForTriagedAsync(storyId);

        var resp = await _client.GetAsync($"/api/stories/{storyId}");
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Conductor's StoryRefinementDTO decoder expects:
        //   refinement.refinedDescription, acceptanceCriteria, risks,
        //   testPlan, classification.{priority,complexity,risk,
        //   suggestedApproach}, refineVersion, refinedAt, refinedBy.
        Assert.True(root.TryGetProperty("refinement", out var refinement),
            "DTO must surface a 'refinement' object once triaged.");
        Assert.True(refinement.TryGetProperty("refinedDescription", out _));
        Assert.True(refinement.TryGetProperty("acceptanceCriteria", out var ac));
        Assert.Equal(JsonValueKind.Array, ac.ValueKind);
        Assert.True(refinement.TryGetProperty("risks", out var risks));
        Assert.Equal(JsonValueKind.Array, risks.ValueKind);
        Assert.True(refinement.TryGetProperty("testPlan", out var tp));
        Assert.Equal(JsonValueKind.Array, tp.ValueKind);
        Assert.True(refinement.TryGetProperty("refineVersion", out _));
        Assert.True(refinement.TryGetProperty("refinedAt", out _));
        Assert.True(refinement.TryGetProperty("refinedBy", out _));

        Assert.True(refinement.TryGetProperty("classification", out var cls));
        Assert.True(cls.TryGetProperty("priority", out var prio));
        Assert.Contains(prio.GetString(), new[] { "p0", "p1", "p2", "p3" });
        Assert.True(cls.TryGetProperty("complexity", out var cx));
        Assert.Contains(cx.GetString(), new[] { "trivial", "small", "medium", "large", "xl" });
        Assert.True(cls.TryGetProperty("risk", out var rk));
        Assert.Contains(rk.GetString(), new[] { "low", "medium", "high" });
        Assert.True(cls.TryGetProperty("suggestedApproach", out _));

        // Conductor's TriageState decoder requires PascalCase kind +
        // (for Triaged/Obsolete) version + at.
        Assert.True(root.TryGetProperty("triageState", out var ts));
        Assert.Equal("Triaged", ts.GetProperty("kind").GetString());
        Assert.True(ts.TryGetProperty("version", out _));
        Assert.True(ts.TryGetProperty("at", out _));
    }

    [Fact]
    public async Task Refine_NotTriagedStory_HasTriageStateKindNotTriaged()
    {
        var storyId = await SeedStoryAsync("dev-user");
        var resp = await _client.GetAsync($"/api/stories/{storyId}");
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var ts = doc.RootElement.GetProperty("triageState");
        Assert.Equal("NotTriaged", ts.GetProperty("kind").GetString());
        // refinement must be absent (null) for an unrefined story
        Assert.True(doc.RootElement.TryGetProperty("refinement", out var refinement));
        Assert.Equal(JsonValueKind.Null, refinement.ValueKind);
    }

    private sealed record RefineRequest(
        string? Instructions = null,
        string? AgentId = null);
}
