// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

public class LlmSettingsControllerTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LlmSettingsControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<LlmSettingDto> CreateOneAsync(
        string name = "t",
        string provider = "openai",
        string apiKey = "sk-test",
        string model = "gpt-4o",
        string? baseUrl = null,
        bool? isDefault = null)
    {
        var response = await _client.PostAsJsonAsync("/api/llm-settings",
            new CreateLlmSettingRequest(name, provider, apiKey, model, baseUrl, isDefault));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<LlmSettingDto>(JsonOptions);
        return dto!;
    }

    [Fact]
    public async Task Post_CreatesSetting_Returns201()
    {
        var dto = await CreateOneAsync(name: "post-created", apiKey: "secret-key");
        Assert.Equal("OpenAI", dto.Provider);
        Assert.Equal("dev-user", dto.OwnerUserId);
    }

    [Fact]
    public async Task Post_ResponseBodyNeverExposesApiKey()
    {
        var response = await _client.PostAsJsonAsync("/api/llm-settings",
            new CreateLlmSettingRequest("no-leak", "openai", "SUPER_SECRET_KEY", "gpt-4o", null, null));
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SUPER_SECRET_KEY", raw);
        Assert.DoesNotContain("apiKey", raw);
    }

    [Fact]
    public async Task Post_InvalidProvider_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/llm-settings",
            new CreateLlmSettingRequest("bad", "gemini", "k", "m", null, null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_InvalidBaseUrl_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/llm-settings",
            new CreateLlmSettingRequest("bad", "openai", "k", "m", "ftp://nope.local", null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_ListsOnlyCallersSettings()
    {
        await SeedOtherUserSettingAsync();
        await CreateOneAsync(name: "mine-only");

        var response = await _client.GetAsync("/api/llm-settings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<LlmSettingDto>>(JsonOptions);
        Assert.NotNull(list);
        Assert.All(list!, dto => Assert.Equal("dev-user", dto.OwnerUserId));
    }

    [Fact]
    public async Task GetById_OtherUsersRow_Returns404()
    {
        var foreignId = await SeedOtherUserSettingAsync();
        var response = await _client.GetAsync($"/api/llm-settings/{foreignId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_UpdatesSetting()
    {
        var dto = await CreateOneAsync(name: "before-patch");

        var response = await _client.PatchAsJsonAsync($"/api/llm-settings/{dto.Id}",
            new UpdateLlmSettingRequest("after-patch", null, null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<LlmSettingDto>(JsonOptions);
        Assert.Equal("after-patch", updated!.Name);
    }

    [Fact]
    public async Task Patch_OtherUsersRow_Returns404()
    {
        var foreignId = await SeedOtherUserSettingAsync();
        var response = await _client.PatchAsJsonAsync($"/api/llm-settings/{foreignId}",
            new UpdateLlmSettingRequest("hijack", null, null, null, null, null));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OwnersRow_Returns204()
    {
        var dto = await CreateOneAsync(name: "to-delete");
        var response = await _client.DeleteAsync($"/api/llm-settings/{dto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OtherUsersRow_Returns404()
    {
        var foreignId = await SeedOtherUserSettingAsync();
        var response = await _client.DeleteAsync($"/api/llm-settings/{foreignId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetDefault_PromotesSetting_Returns204()
    {
        var first = await CreateOneAsync(name: "first-default");
        var second = await CreateOneAsync(name: "to-become-default", provider: "anthropic");

        var response = await _client.PostAsync($"/api/llm-settings/{second.Id}/set-default", content: null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Validate via the list endpoint: only `second` is default.
        var listResponse = await _client.GetAsync("/api/llm-settings");
        var list = await listResponse.Content.ReadFromJsonAsync<List<LlmSettingDto>>(JsonOptions);
        Assert.True(list!.Single(l => l.Id == second.Id).IsDefault);
        Assert.False(list!.Single(l => l.Id == first.Id).IsDefault);
    }

    [Fact]
    public async Task SetDefault_OtherUsersRow_Returns404()
    {
        var foreignId = await SeedOtherUserSettingAsync();
        var response = await _client.PostAsync($"/api/llm-settings/{foreignId}/set-default", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> SeedOtherUserSettingAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = new LlmSetting
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "other-user",
            Name = $"other-{Guid.NewGuid():N}",
            Provider = LlmProvider.OpenAI,
            ApiKey = "secret::other",
            Model = "gpt-4o"
        };
        db.LlmSettings.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }
}
