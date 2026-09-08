// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Text;
using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Infrastructure.External;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class AndySettingsClientTests
{
    // MARK: - LocalSettingsClient (fallback)

    [Fact]
    public async Task Local_GetString_ReturnsValueFromConfig()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AndyCodeIndex:ApiBaseUrl"] = "http://code-index:8080"
        });
        var client = new LocalSettingsClient(config);

        var result = await client.GetAsync<string>("andy.issues.codeIndex.baseUrl");

        Assert.Equal("http://code-index:8080", result);
    }

    [Fact]
    public async Task Local_GetInt_ReturnsValueFromConfig()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AzureDevOps:PullIntervalSeconds"] = "600"
        });
        var client = new LocalSettingsClient(config);

        var result = await client.GetAsync<int>("andy.issues.azureDevops.pullIntervalSeconds");

        Assert.Equal(600, result);
    }

    [Fact]
    public async Task Local_MissingKey_ReturnsDefault()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var client = new LocalSettingsClient(config);

        var result = await client.GetAsync<string>("andy.issues.nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task Local_GetBatch_ReturnsOnlyExistingKeys()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AndyCodeIndex:ApiBaseUrl"] = "http://ci:8080",
            ["AndyContainers:BaseUrl"] = "http://ctr:9090"
        });
        var client = new LocalSettingsClient(config);

        var result = await client.GetBatchAsync(new[]
        {
            "andy.issues.codeIndex.baseUrl",
            "andy.issues.containers.baseUrl",
            "andy.issues.nonexistent"
        });

        Assert.Equal(2, result.Count);
        Assert.Equal("http://ci:8080", result["andy.issues.codeIndex.baseUrl"]);
        Assert.Equal("http://ctr:9090", result["andy.issues.containers.baseUrl"]);
    }

    // MARK: - AndySettingsClient (HTTP + cache)

    [Fact]
    public async Task Http_GetAsync_CachesAcrossRepeatedCalls()
    {
        var callCount = 0;
        var handler = new CountingHandler(() =>
        {
            callCount++;
            return Respond(HttpStatusCode.OK, new { key = "test.key", effectiveValue = "\"cached-value\"" });
        });
        var client = CreateHttpClient(handler);

        var first = await client.GetAsync<string>("test.key");
        var second = await client.GetAsync<string>("test.key");

        Assert.Equal("cached-value", first);
        Assert.Equal("cached-value", second);
        Assert.Equal(1, callCount); // Second call served from cache
    }

    [Fact]
    public async Task Http_GetAsync_NotFound_CachesAbsence()
    {
        var callCount = 0;
        var handler = new CountingHandler(() =>
        {
            callCount++;
            return Respond(HttpStatusCode.NotFound);
        });
        var client = CreateHttpClient(handler);

        var first = await client.GetAsync<string>("missing.key");
        var second = await client.GetAsync<string>("missing.key");

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, callCount); // Absence cached
    }

    [Fact]
    public async Task Http_GetBatch_ReturnsCachedAndFreshValues()
    {
        var callCount = 0;
        var handler = new CountingHandler(() =>
        {
            callCount++;
            return Respond(HttpStatusCode.OK, new[] { new { key = "key1", effectiveValue = "\"v1\"" }, new { key = "key2", effectiveValue = "\"v2\"" } });
        });
        var client = CreateHttpClient(handler);

        var result = await client.GetBatchAsync(new[] { "key1", "key2" });

        Assert.Equal(2, result.Count);
        Assert.Equal("v1", result["key1"]);
        Assert.Equal("v2", result["key2"]);
        Assert.Equal(1, callCount);

        // Second call should be fully cached
        var result2 = await client.GetBatchAsync(new[] { "key1", "key2" });
        Assert.Equal(2, result2.Count);
        Assert.Equal(1, callCount); // No additional HTTP call
        Assert.Equal("v1", await client.GetAsync<string>("key1"));
    }

    [Fact]
    public async Task Http_GetAsync_ServerError_ReturnsDefault()
    {
        var handler = new CountingHandler(() =>
            Respond(HttpStatusCode.InternalServerError));
        var client = CreateHttpClient(handler);

        var result = await client.GetAsync<string>("broken.key");

        Assert.Null(result);
    }

    [Fact]
    public async Task Http_Settings_UseEffectiveResolutionContract()
    {
        var handler = new RequestHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/effective/resolve", request.RequestUri!.AbsolutePath);
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("andy-issues:sandbox:max-per-user", doc.RootElement.GetProperty("key").GetString());
            Assert.Equal("andy-issues", doc.RootElement.GetProperty("context").GetProperty("applicationCode").GetString());
            return Respond(HttpStatusCode.OK, new { key = "andy-issues:sandbox:max-per-user", effectiveValue = "2", isValid = true });
        });
        Assert.Equal(2, await CreateHttpClient(handler).GetAsync<int?>("andy-issues:sandbox:max-per-user"));
    }

    [Fact]
    public async Task Http_Secret_UsesSecretsEndpointAndDoesNotCachePlaintext()
    {
        var calls = 0;
        var handler = new RequestHandler(request =>
        {
            Assert.Equal("/api/secrets/sourceControl.github.pat?scopeType=Machine", request.RequestUri!.PathAndQuery);
            calls++;
            return Respond(HttpStatusCode.OK, new { definitionKey = "sourceControl.github.pat", value = $"rotated-{calls}" });
        });
        var client = CreateHttpClient(handler);
        var store = new Andy.Issues.Infrastructure.Services.SecretStore(client, NullLogger<Andy.Issues.Infrastructure.Services.SecretStore>.Instance);

        Assert.Equal("rotated-1", await store.ResolveAsync("secret::sourceControl.github.pat"));
        Assert.Equal("rotated-2", await store.ResolveAsync("secret::sourceControl.github.pat"));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Http_Secret_FailedReadNeverReturnsErrorBodyAsCredential(HttpStatusCode status)
    {
        var handler = new CountingHandler(() => Respond(status, new { value = "not-a-token" }));
        Assert.Null(await CreateHttpClient(handler).GetSecretAsync("sourceControl.github.pat"));
    }

    [Fact]
    public async Task Http_Secret_CancellationPropagates()
    {
        var handler = new RequestHandler(_ => throw new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateHttpClient(handler).GetSecretAsync("sourceControl.github.pat"));
    }

    private sealed class RequestHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    // Helpers

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static AndySettingsClient CreateHttpClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://andy-settings.local/") };
        var factory = new SingleClientFactory(http);
        return new AndySettingsClient(factory, NullLogger<AndySettingsClient>.Instance);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, object? body = null)
    {
        var json = body is not null ? JsonSerializer.Serialize(body) : "{}";
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;
        public CountingHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond());
    }
}
