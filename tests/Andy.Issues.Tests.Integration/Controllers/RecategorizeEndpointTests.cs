// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

/// <summary>
/// Exercises <c>POST /api/repositories/{id}/recategorize</c> with the
/// REAL <c>BacklogRecategorizeService</c> in the pipeline. The only
/// thing faked below the service is the outbound LLM HTTP call — the
/// named <c>"LlmProvider"</c> client gets a stub primary handler
/// (mirroring how the service reaches the provider in production), so
/// the full chain controller → service → LlmChatCompletion → parse →
/// EF apply runs for real.
/// </summary>
public class RecategorizeEndpointTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    /// <summary>Process-wide Seq source so parallel seeds can't collide on the unique index.</summary>
    private static long _seqCounter = 500_000;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly StubLlmHandler _llmHandler = new();

    public RecategorizeEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient("LlmProvider")
                    .ConfigurePrimaryHttpMessageHandler(() => _llmHandler);
            }));
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Recategorize_HappyPath_Returns200WithPinnedContractShape()
    {
        var repoId = await SeedRepoWithUncategorizedAsync(withLlm: true);
        _llmHandler.AssistantContent = """
            { "epics": [], "features": [], "assignments": [
                { "item": "gh:12", "role": "story", "parentRef": "existing:45" } ] }
            """;

        var response = await _client.PostAsJsonAsync(
            $"/api/repositories/{repoId}/recategorize",
            new { applyToGitHub = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // The PINNED contract — the Conductor client is built against
        // these exact field names. Every one must be present.
        Assert.Equal(1, body.GetProperty("classified").GetInt32());
        Assert.Equal(0, body.GetProperty("epicsCreated").GetInt32());
        Assert.Equal(0, body.GetProperty("featuresCreated").GetInt32());
        Assert.Equal(1, body.GetProperty("storiesReparented").GetInt32());
        Assert.Equal(0, body.GetProperty("labelsApplied").GetInt32());
        Assert.Equal(0, body.GetProperty("subIssuesLinked").GetInt32());
        Assert.Equal(0, body.GetProperty("githubIssuesCreated").GetInt32());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("errors").ValueKind);
        Assert.Empty(body.GetProperty("errors").EnumerateArray());

        // And the classification really applied: the story now hangs
        // under the real feature, the emptied buckets are healed.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var story = db.UserStories.Single(s => s.ExternalId == "gh:12"
            && s.Feature.Epic.RepositoryId == repoId);
        Assert.Equal("gh:45", db.Features.Single(f => f.Id == story.FeatureId).ExternalId);
        Assert.False(db.Epics.Any(e => e.RepositoryId == repoId && e.ExternalId == "gh:uncategorized"));
    }

    [Fact]
    public async Task Recategorize_NoLlmSetting_Returns400WithPinnedErrorShape()
    {
        var repoId = await SeedRepoWithUncategorizedAsync(withLlm: false);

        var response = await _client.PostAsJsonAsync(
            $"/api/repositories/{repoId}/recategorize",
            new { applyToGitHub = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("no_llm_setting", body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task Recategorize_LlmFailure_Returns502WithPinnedErrorShape()
    {
        var repoId = await SeedRepoWithUncategorizedAsync(withLlm: true);
        _llmHandler.StatusCode = HttpStatusCode.InternalServerError;
        try
        {
            var response = await _client.PostAsJsonAsync(
                $"/api/repositories/{repoId}/recategorize",
                new { applyToGitHub = false });

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("llm_failed", body.GetProperty("error").GetString());
            Assert.False(string.IsNullOrEmpty(body.GetProperty("message").GetString()));
        }
        finally
        {
            _llmHandler.StatusCode = HttpStatusCode.OK;
        }
    }

    [Fact]
    public async Task Recategorize_UnknownRepo_Returns404()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/repositories/{Guid.NewGuid()}/recategorize",
            new { applyToGitHub = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // MARK: - Seeding

    /// <summary>
    /// Seeds a GitHub repo owned by the test principal with a real
    /// epic (gh:32) → feature (gh:45) pair, the synthetic Uncategorized
    /// epic+feature, and one uncategorized story (gh:12).
    /// </summary>
    private async Task<Guid> SeedRepoWithUncategorizedAsync(bool withLlm)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        LlmSetting? llm = null;
        if (withLlm)
        {
            llm = new LlmSetting
            {
                Id = Guid.NewGuid(),
                OwnerUserId = TestAuthHandler.UserId,
                Name = "test-llm",
                Provider = LlmProvider.OpenAI,
                ApiKey = "sk-test",
                Model = "gpt-4o"
            };
            db.LlmSettings.Add(llm);
        }

        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = TestAuthHandler.UserId,
            Name = $"recat-{Guid.NewGuid():N}",
            Provider = RepositoryProvider.GitHub,
            CloneUrl = $"https://github.com/test/recat-{Guid.NewGuid():N}.git",
            LlmSettingId = llm?.Id
        };
        db.Repositories.Add(repo);

        var realEpic = new Epic
        {
            Id = Guid.NewGuid(),
            Seq = Interlocked.Increment(ref _seqCounter),
            RepositoryId = repo.Id,
            Title = "Platform Hardening",
            Order = 32,
            ExternalId = "gh:32"
        };
        db.Epics.Add(realEpic);
        db.Features.Add(new Feature
        {
            Id = Guid.NewGuid(),
            Seq = Interlocked.Increment(ref _seqCounter),
            EpicId = realEpic.Id,
            Title = "Auth Flow",
            Order = 45,
            ExternalId = "gh:45"
        });

        var uncatEpic = new Epic
        {
            Id = Guid.NewGuid(),
            Seq = Interlocked.Increment(ref _seqCounter),
            RepositoryId = repo.Id,
            Title = "Uncategorized",
            Order = int.MaxValue,
            ExternalId = "gh:uncategorized"
        };
        db.Epics.Add(uncatEpic);
        var uncatFeature = new Feature
        {
            Id = Guid.NewGuid(),
            Seq = Interlocked.Increment(ref _seqCounter),
            EpicId = uncatEpic.Id,
            Title = "Uncategorized",
            Order = int.MaxValue,
            ExternalId = "gh:uncategorized"
        };
        db.Features.Add(uncatFeature);
        db.UserStories.Add(new UserStory
        {
            Id = Guid.NewGuid(),
            Seq = Interlocked.Increment(ref _seqCounter),
            FeatureId = uncatFeature.Id,
            Title = "Login button",
            Order = 12,
            ExternalId = "gh:12"
        });

        await db.SaveChangesAsync();
        return repo.Id;
    }

    // MARK: - LLM stub

    /// <summary>
    /// Primary handler for the named "LlmProvider" client. Impersonates
    /// the OpenAI chat-completions wire shape (the seeded LlmSetting is
    /// Provider=OpenAI) or fails with a configurable status code.
    /// </summary>
    private sealed class StubLlmHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string AssistantContent { get; set; } = "{}";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (StatusCode != HttpStatusCode.OK)
                return Task.FromResult(new HttpResponseMessage(StatusCode)
                {
                    Content = new StringContent("upstream error")
                });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    choices = new[]
                    {
                        new { message = new { role = "assistant", content = AssistantContent } }
                    }
                })
            });
        }
    }
}
