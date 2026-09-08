// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class LlmSecretStoreTests
{
    [Fact]
    public async Task PersistentKeyRing_SurvivesNewProviderAndRejectsWrongKeyRing()
    {
        var path = Path.Combine(Path.GetTempPath(), "andy-llm-key-test-" + Guid.NewGuid());
        try
        {
            var provider = DataProtectionProvider.Create(new DirectoryInfo(path));
            var store = new LlmSecretStore(provider, new Legacy());
            var value = await store.StoreAsync("test", new string('x', 2048));
            Assert.DoesNotContain(new string('x', 20), value);
            var reopened = new LlmSecretStore(DataProtectionProvider.Create(new DirectoryInfo(path)), new Legacy());
            Assert.Equal(new string('x', 2048), await reopened.ResolveAsync(value));
            var wrongKeyRing = new LlmSecretStore(new EphemeralDataProtectionProvider(), new Legacy());
            Assert.Null(await wrongKeyRing.ResolveAsync(value));
            Assert.Equal("resolved-test-reference", await reopened.ResolveAsync("secret::test"));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
    }
    [Theory]
    [InlineData(Andy.Issues.Domain.Enums.LlmProvider.OpenAI)]
    [InlineData(Andy.Issues.Domain.Enums.LlmProvider.Anthropic)]
    public async Task ProviderCalls_ReceiveResolvedKeyInsteadOfCiphertext(Andy.Issues.Domain.Enums.LlmProvider provider)
    {
        var secrets = new LlmSecretStore(new EphemeralDataProtectionProvider(), new Legacy());
        var stored = await secrets.StoreAsync("test", "test-provider-key");
        using var http = new HttpClient(new ProviderHandler(provider));
        var setting = new Andy.Issues.Domain.Entities.LlmSetting { Provider = provider, Model = "test-model", ApiKey = stored };
        Assert.Equal("ok", await LlmChatCompletion.CompleteAsync(new Factory(http), setting, "system", "user", 10, 0, false, CancellationToken.None, secrets));
        Assert.Equal(stored, setting.ApiKey);
    }
    private sealed class Factory(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => http;
    }
    private sealed class ProviderHandler(Andy.Issues.Domain.Enums.LlmProvider provider) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = provider == Andy.Issues.Domain.Enums.LlmProvider.Anthropic
                ? Assert.Single(request.Headers.GetValues("x-api-key")) : request.Headers.Authorization?.Parameter;
            Assert.Equal("test-provider-key", key);
            var json = provider == Andy.Issues.Domain.Enums.LlmProvider.Anthropic
                ? "{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}" : "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
        }
    }
    private sealed class Legacy : ISecretStore
    {
        public Task<string?> ResolveAsync(string? valueOrRef, CancellationToken ct = default) =>
            Task.FromResult(valueOrRef == "secret::test" ? "resolved-test-reference" : valueOrRef);
        public Task<string> StoreAsync(string key, string value, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
