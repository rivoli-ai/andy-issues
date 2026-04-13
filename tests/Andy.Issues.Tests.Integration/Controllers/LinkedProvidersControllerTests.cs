// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Requests;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

public class LinkedProvidersControllerTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LinkedProvidersControllerTests(TestWebApplicationFactory factory)
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
    public async Task Post_CreatesOrUpdatesLinkedProvider()
    {
        var response = await _client.PostAsJsonAsync("/api/linked-providers",
            new CreateLinkedProviderRequest("github", "ghp_test", null, null, "testuser"));

        // First call may be Created (201) or Updated (200) depending on
        // test execution order — both are valid upsert outcomes.
        Assert.True(
            response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"Expected 201 or 200 but got {(int)response.StatusCode}.");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("GitHub", body.GetProperty("provider").GetString());
        Assert.Equal("testuser", body.GetProperty("accountLogin").GetString());
    }

    [Fact]
    public async Task Post_UpdatesExistingProvider()
    {
        await _client.PostAsJsonAsync("/api/linked-providers",
            new CreateLinkedProviderRequest("azuredevops", "pat1", null, null, "old"));

        var response = await _client.PostAsJsonAsync("/api/linked-providers",
            new CreateLinkedProviderRequest("azuredevops", "pat2", null, null, "new"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("new", body.GetProperty("accountLogin").GetString());
    }

    [Fact]
    public async Task Post_InvalidProvider_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/linked-providers",
            new CreateLinkedProviderRequest("gitlab", "token", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_ListsProviders()
    {
        await _client.PostAsJsonAsync("/api/linked-providers",
            new CreateLinkedProviderRequest("github", "ghp_list", null, null, "lister"));

        var response = await _client.GetAsync("/api/linked-providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.True(body.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Get_DoesNotExposeTokens()
    {
        await _client.PostAsJsonAsync("/api/linked-providers",
            new CreateLinkedProviderRequest("github", "SUPER_SECRET", null, null, null));

        var response = await _client.GetAsync("/api/linked-providers");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("SUPER_SECRET", raw);
    }

    [Fact]
    public async Task Delete_RemovesProvider()
    {
        await _client.PostAsJsonAsync("/api/linked-providers",
            new CreateLinkedProviderRequest("github", "ghp_del", null, null, null));

        var response = await _client.DeleteAsync("/api/linked-providers/github");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_NonexistentReturns404()
    {
        var response = await _client.DeleteAsync("/api/linked-providers/gitlab");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // MARK: - PAT entry helper

    [Fact]
    public async Task PostPat_ValidGitHubPat_LinkSucceeds()
    {
        var response = await _client.PostAsJsonAsync("/api/linked-providers/pat",
            new LinkPatRequest("github", "ghp_valid"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("GitHub", body.GetProperty("provider").GetString());
        Assert.Equal("fake-gh-user", body.GetProperty("accountLogin").GetString());
    }

    [Fact]
    public async Task PostPat_InvalidProvider_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/linked-providers/pat",
            new LinkPatRequest("gitlab", "pat"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostPat_InvalidPat_Returns400()
    {
        _factory.FakeGitHubClient.CurrentUserResult = null;

        var response = await _client.PostAsJsonAsync("/api/linked-providers/pat",
            new LinkPatRequest("github", "bad-token"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("invalid", body.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);

        // Reset for other tests
        _factory.FakeGitHubClient.CurrentUserResult = new Andy.Issues.Application.Interfaces.GitHubUserInfo("fake-gh-user");
    }
}
