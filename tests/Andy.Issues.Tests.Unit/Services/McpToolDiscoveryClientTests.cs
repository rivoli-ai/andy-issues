// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Text;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Infrastructure.External;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class McpToolDiscoveryClientTests
{
    private static McpToolDiscoveryClient NewClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        var http = new HttpClient(new StubHandler(handler));
        return new McpToolDiscoveryClient(http, NullLogger<McpToolDiscoveryClient>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task Discover_HappyPath_ReturnsTools()
    {
        var calls = new List<string>();
        var client = NewClient(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            if (body.Contains("\"initialize\""))
            {
                calls.Add("initialize");
                return Json(HttpStatusCode.OK, """
                    { "jsonrpc":"2.0","id":1,"result":{ "protocolVersion":"2024-11-05" } }
                    """);
            }

            calls.Add("tools/list");
            return Json(HttpStatusCode.OK, """
                {
                  "jsonrpc":"2.0","id":2,
                  "result":{
                    "tools":[
                      { "name":"search", "description":"search repo", "inputSchema":{"type":"object"} },
                      { "name":"fetch",  "description":null,           "inputSchema":{"type":"object","properties":{}} }
                    ]
                  }
                }
                """);
        });

        var result = await client.DiscoverAsync("https://mcp.example.com/jsonrpc", null);
        Assert.Equal(McpToolDiscoveryOutcome.Ok, result.Outcome);
        Assert.Equal(2, result.Tools!.Count);
        Assert.Equal("search", result.Tools[0].Name);
        Assert.Equal("search repo", result.Tools[0].Description);
        Assert.NotNull(result.Tools[0].InputSchema);
        Assert.Equal(new[] { "initialize", "tools/list" }, calls);
    }

    [Fact]
    public async Task Discover_ForwardsConfiguredHeaders()
    {
        string? seenAuth = null;
        var client = NewClient(req =>
        {
            seenAuth = req.Headers.TryGetValues("Authorization", out var v)
                ? string.Join(",", v)
                : null;
            return Task.FromResult(Json(HttpStatusCode.OK, """
                { "jsonrpc":"2.0","id":1,"result":{ "tools":[] } }
                """));
        });

        await client.DiscoverAsync("https://mcp.example.com",
            headersJson: "{\"Authorization\":\"Bearer hush\"}");
        Assert.Equal("Bearer hush", seenAuth);
    }

    [Fact]
    public async Task Discover_Non200_ReturnsHttpError()
    {
        var client = NewClient(_ =>
            Task.FromResult(Json(HttpStatusCode.Unauthorized, "no")));
        var result = await client.DiscoverAsync("https://mcp.example.com", null);
        Assert.Equal(McpToolDiscoveryOutcome.HttpError, result.Outcome);
        Assert.Contains("401", result.Error);
    }

    [Fact]
    public async Task Discover_MalformedJsonRpcError_ReturnsMalformed()
    {
        var callCount = 0;
        var client = NewClient(req =>
        {
            callCount++;
            return Task.FromResult(Json(HttpStatusCode.OK, callCount == 1
                ? """{ "jsonrpc":"2.0","id":1,"result":{} }"""
                : """{ "jsonrpc":"2.0","id":2,"error":{ "code":-32601,"message":"not implemented"} }"""));
        });
        var result = await client.DiscoverAsync("https://mcp.example.com", null);
        Assert.Equal(McpToolDiscoveryOutcome.MalformedResponse, result.Outcome);
        Assert.Contains("not implemented", result.Error);
    }

    [Fact]
    public async Task Discover_ResultWithoutTools_ReturnsMalformed()
    {
        var client = NewClient(_ =>
            Task.FromResult(Json(HttpStatusCode.OK, """{ "jsonrpc":"2.0","id":1,"result":{} }""")));
        var result = await client.DiscoverAsync("https://mcp.example.com", null);
        Assert.Equal(McpToolDiscoveryOutcome.MalformedResponse, result.Outcome);
    }

    [Fact]
    public async Task Discover_InvalidUrl_ReturnsMalformed()
    {
        var client = NewClient(_ => Task.FromResult(Json(HttpStatusCode.OK, "{}")));
        var result = await client.DiscoverAsync("not-a-url", null);
        Assert.Equal(McpToolDiscoveryOutcome.MalformedResponse, result.Outcome);
    }

    [Fact]
    public async Task Discover_MalformedHeadersJson_ReturnsMalformed()
    {
        var client = NewClient(_ => Task.FromResult(Json(HttpStatusCode.OK, "{}")));
        var result = await client.DiscoverAsync("https://mcp.example.com", headersJson: "this is not json");
        Assert.Equal(McpToolDiscoveryOutcome.MalformedResponse, result.Outcome);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request);
    }
}
