// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

public class GenerateBacklogTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public GenerateBacklogTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GenerateBacklog_RepoNotFound_Returns404()
    {
        var response = await _client.PostAsync(
            $"/api/repositories/{Guid.NewGuid()}/generate-backlog", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GenerateBacklog_NoLlmSetting_Returns400()
    {
        var repoId = await SeedRepoAsync(withLlm: false);

        var response = await _client.PostAsync(
            $"/api/repositories/{repoId}/generate-backlog", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("LLM", body.GetProperty("error").GetString()!);
    }

    [Fact]
    public async Task GenerateBacklog_CodeIndexNotReady_Returns502()
    {
        var repoId = await SeedRepoAsync(withLlm: true);
        _factory.FakeCodeIndexClient.StatusResult =
            new CodeIndexStatusResult(CodeIndexQueryOutcome.ServiceUnavailable, null, null, "not ready");

        var response = await _client.PostAsync(
            $"/api/repositories/{repoId}/generate-backlog", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("codeIndexNotReady", body.GetProperty("reason").GetString());

        // Reset for other tests
        _factory.FakeCodeIndexClient.Reset();
    }

    private async Task<Guid> SeedRepoAsync(bool withLlm)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        LlmSetting? llm = null;
        if (withLlm)
        {
            llm = new LlmSetting
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "dev-user",
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
            OwnerUserId = "dev-user",
            Name = "test-repo",
            CloneUrl = $"https://github.com/test/{Guid.NewGuid()}.git",
            LlmSettingId = llm?.Id
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        return repo.Id;
    }
}
