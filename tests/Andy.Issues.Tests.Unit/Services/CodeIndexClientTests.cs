// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Text;
using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Infrastructure.External;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class CodeIndexClientTests
{
    private static CodeIndexClient CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://code-index.local/") };
        return new CodeIndexClient(http, NullLogger<CodeIndexClient>.Instance);
    }

    private static StubHandler Respond(HttpStatusCode status, object? body = null)
    {
        var json = body is not null ? JsonSerializer.Serialize(body) : "{}";
        return new StubHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }

    // MARK: - RegisterAsync

    [Fact]
    public async Task Register_Success_ReturnsRegistered()
    {
        using var handler = Respond(HttpStatusCode.Created, new { indexId = "idx-42" });
        var client = CreateClient(handler);

        var result = await client.RegisterAsync("https://github.com/x/y.git", "main");

        Assert.Equal(CodeIndexRegistrationOutcome.Registered, result.Outcome);
        Assert.Equal("idx-42", result.IndexId);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Register_Conflict_ReturnsAlreadyRegistered()
    {
        using var handler = Respond(HttpStatusCode.Conflict, new { indexId = "idx-existing" });
        var client = CreateClient(handler);

        var result = await client.RegisterAsync("https://github.com/x/y.git", "main");

        Assert.Equal(CodeIndexRegistrationOutcome.AlreadyRegistered, result.Outcome);
        Assert.Equal("idx-existing", result.IndexId);
    }

    [Fact]
    public async Task Register_BadRequest_ReturnsInvalidUrl()
    {
        using var handler = Respond(HttpStatusCode.BadRequest, new { error = "bad clone URL" });
        var client = CreateClient(handler);

        var result = await client.RegisterAsync("not-a-url", "main");

        Assert.Equal(CodeIndexRegistrationOutcome.InvalidUrl, result.Outcome);
        Assert.Equal("bad clone URL", result.Error);
    }

    [Fact]
    public async Task Register_ServerError_ReturnsServiceUnavailable()
    {
        using var handler = Respond(HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        var result = await client.RegisterAsync("https://github.com/x/y.git", "main");

        Assert.Equal(CodeIndexRegistrationOutcome.ServiceUnavailable, result.Outcome);
        Assert.Contains("500", result.Error!);
    }

    [Fact]
    public async Task Register_HttpException_ReturnsServiceUnavailable()
    {
        using var handler = new ThrowingHandler(new HttpRequestException("connection refused"));
        var client = CreateClient(handler);

        var result = await client.RegisterAsync("https://github.com/x/y.git", "main");

        Assert.Equal(CodeIndexRegistrationOutcome.ServiceUnavailable, result.Outcome);
        Assert.Contains("connection refused", result.Error!);
    }

    // MARK: - GetStatusAsync

    [Fact]
    public async Task GetStatus_Success_ReturnsOkWithSummary()
    {
        using var handler = Respond(HttpStatusCode.OK, new { status = "Indexed", summary = "3 modules, 42 files" });
        var client = CreateClient(handler);

        var result = await client.GetStatusAsync("https://github.com/x/y.git");

        Assert.Equal(CodeIndexQueryOutcome.Ok, result.Outcome);
        Assert.Equal("Indexed", result.Status);
        Assert.Equal("3 modules, 42 files", result.Summary);
    }

    [Fact]
    public async Task GetStatus_NotFound_ReturnsNotFound()
    {
        using var handler = Respond(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var result = await client.GetStatusAsync("https://github.com/x/y.git");

        Assert.Equal(CodeIndexQueryOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task GetStatus_ServerError_ReturnsServiceUnavailable()
    {
        using var handler = Respond(HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        var result = await client.GetStatusAsync("https://github.com/x/y.git");

        Assert.Equal(CodeIndexQueryOutcome.ServiceUnavailable, result.Outcome);
    }

    // MARK: - DeregisterAsync

    [Fact]
    public async Task Deregister_Success_ReturnsTrue()
    {
        using var handler = Respond(HttpStatusCode.NoContent);
        var client = CreateClient(handler);

        var result = await client.DeregisterAsync("https://github.com/x/y.git");

        Assert.True(result);
    }

    [Fact]
    public async Task Deregister_NotFound_ReturnsFalse()
    {
        using var handler = Respond(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var result = await client.DeregisterAsync("https://github.com/x/y.git");

        Assert.False(result);
    }

    [Fact]
    public async Task Deregister_HttpException_ReturnsFalse()
    {
        using var handler = new ThrowingHandler(new HttpRequestException("timeout"));
        var client = CreateClient(handler);

        var result = await client.DeregisterAsync("https://github.com/x/y.git");

        Assert.False(result);
    }

    // Helpers

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_response);

        protected override void Dispose(bool disposing)
        {
            // Don't dispose _response — tests own the lifecycle
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(_exception);
    }
}
