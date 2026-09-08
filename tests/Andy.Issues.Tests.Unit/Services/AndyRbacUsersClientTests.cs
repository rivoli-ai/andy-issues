// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Infrastructure.External;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class AndyRbacUsersClientTests
{
    [Fact]
    public async Task Cache_IsSeparatedByCallerAndFilterAndErrorsAreNotCached()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://rbac/") };
        var client = new AndyRbacUsersClient(new Factory(http), cache);
        await client.ListAsync("alice", "search & name", "admin", -1, 500);
        Assert.Contains("search%20%26%20name", handler.Path);
        Assert.Contains("skip=0&take=200", handler.Path);
        await client.ListAsync("alice", "search & name", "admin", -1, 500);
        Assert.Equal(1, handler.Calls);
        await client.ListAsync("bob", "search & name", "admin", -1, 500);
        Assert.Equal(2, handler.Calls);
        handler.Fail = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => client.ListAsync("bob", "new", null, 0, 50));
        handler.Fail = false;
        await client.ListAsync("bob", "new", null, 0, 50);
        Assert.Equal(4, handler.Calls);
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public string Path { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Path = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.BadGateway : HttpStatusCode.OK)
            { Content = JsonContent.Create(new AdminUsersDto([], 0)) });
        }
    }
}
