// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

/// <summary>
/// Guards against the regression that caused Conductor's help browser to
/// 404 on every topic: <c>content/help/*.md</c> was not being copied into
/// the published output, so <see cref="Andy.Issues.Api.Controllers.HelpController"/>
/// found an empty directory and returned 404 for every slug.
///
/// The csproj now copies <c>content/help/*.md</c> via <c>&lt;Content&gt;</c>
/// items. These tests assert the controller actually serves topics over
/// HTTP — i.e. the directory ships with the binary and the
/// <see cref="Microsoft.Extensions.Hosting.IHostEnvironment"/> resolution
/// in the controller picks it up.
/// </summary>
public class HelpControllerTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly HttpClient _client;

    public HelpControllerTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ListTopics_ReturnsBundledTopics()
    {
        var response = await _client.GetAsync("/api/help/topics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var topics = await response.Content.ReadFromJsonAsync<JsonElement>();
        var slugs = topics.EnumerateArray()
            .Select(t => t.GetProperty("slug").GetString())
            .ToList();

        // The five bundled topics that ship in content/help/. Whenever
        // a topic is added or removed, update this list to match — the
        // intent is to guarantee the publish pipeline carries them, not
        // to police topic content.
        Assert.Contains("getting-started", slugs);
        Assert.Contains("authentication", slugs);
        Assert.Contains("api-access", slugs);
        Assert.Contains("architecture", slugs);
        Assert.Contains("support", slugs);
    }

    [Theory]
    [InlineData("getting-started")]
    [InlineData("authentication")]
    [InlineData("api-access")]
    [InlineData("architecture")]
    [InlineData("support")]
    public async Task GetTopic_BundledSlug_Returns200(string slug)
    {
        var response = await _client.GetAsync($"/api/help/topics/{slug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var topic = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(slug, topic.GetProperty("slug").GetString());
        Assert.False(string.IsNullOrWhiteSpace(topic.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(topic.GetProperty("markdown").GetString()));
    }

    [Fact]
    public async Task GetTopic_UnknownSlug_Returns404()
    {
        var response = await _client.GetAsync("/api/help/topics/this-slug-does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
