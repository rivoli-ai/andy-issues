// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Text;
using System.Text.Json;
using Andy.Issues.Infrastructure.External;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

// #164 — wire-shape coverage for the andy-docs adapter. Verifies the
// HTTP surface our IDocsClient consumes (links list, document
// metadata) and the adapter's response handling.
public class AndyDocsClientAdapterTests
{
    private static AndyDocsClientAdapter CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://andy-docs.local/") };
        return new AndyDocsClientAdapter(http, NullLogger<AndyDocsClientAdapter>.Instance);
    }

    private static StubHandler RespondJson(HttpStatusCode status, object body)
    {
        var json = JsonSerializer.Serialize(body);
        return new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }

    private static StubHandler RespondStatus(HttpStatusCode status)
    {
        return new StubHandler(_ => new HttpResponseMessage(status));
    }

    // ── VerifyLinkAsync ────────────────────────────────────────────────

    [Fact]
    public async Task VerifyLink_LinkInResponse_ReturnsTrue()
    {
        var linkId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var issueId = Guid.NewGuid();

        using var handler = RespondJson(HttpStatusCode.OK, new[]
        {
            new
            {
                id = linkId,
                documentId,
                targetType = "Issue",
                targetId = issueId.ToString(),
                role = "Input",
                createdAt = DateTime.UtcNow,
                createdBy = Guid.NewGuid()
            }
        });
        var client = CreateClient(handler);

        var ok = await client.VerifyLinkAsync(linkId, "issue", issueId);

        Assert.True(ok);
    }

    [Fact]
    public async Task VerifyLink_LinkNotInResponse_ReturnsFalse()
    {
        var documentId = Guid.NewGuid();
        var issueId = Guid.NewGuid();

        using var handler = RespondJson(HttpStatusCode.OK, new[]
        {
            new
            {
                id = Guid.NewGuid(),  // a different link
                documentId,
                targetType = "Issue",
                targetId = issueId.ToString(),
                role = "Input",
                createdAt = DateTime.UtcNow,
                createdBy = Guid.NewGuid()
            }
        });
        var client = CreateClient(handler);

        var ok = await client.VerifyLinkAsync(Guid.NewGuid(), "issue", issueId);

        Assert.False(ok);
    }

    [Fact]
    public async Task VerifyLink_EmptyResponse_ReturnsFalse()
    {
        using var handler = RespondJson(HttpStatusCode.OK, Array.Empty<object>());
        var client = CreateClient(handler);

        var ok = await client.VerifyLinkAsync(Guid.NewGuid(), "issue", Guid.NewGuid());

        Assert.False(ok);
    }

    [Fact]
    public async Task VerifyLink_PassesTargetTypeAndIdInQueryString()
    {
        var capturedUrl = "";
        using var handler = new StubHandler(req =>
        {
            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        });
        var client = CreateClient(handler);
        var issueId = Guid.NewGuid();

        await client.VerifyLinkAsync(Guid.NewGuid(), "issue", issueId);

        Assert.Contains("api/links?", capturedUrl);
        Assert.Contains("targetType=issue", capturedUrl);
        Assert.Contains($"targetId={issueId}", capturedUrl);
    }

    [Fact]
    public async Task VerifyLink_NotFound_ReturnsFalse()
    {
        using var handler = RespondStatus(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var ok = await client.VerifyLinkAsync(Guid.NewGuid(), "issue", Guid.NewGuid());

        Assert.False(ok);
    }

    [Fact]
    public async Task VerifyLink_ServerError_ReturnsFalse()
    {
        using var handler = RespondStatus(HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        var ok = await client.VerifyLinkAsync(Guid.NewGuid(), "issue", Guid.NewGuid());

        Assert.False(ok);
    }

    [Fact]
    public async Task VerifyLink_NetworkException_ReturnsFalse()
    {
        using var handler = new ThrowingHandler(new HttpRequestException("connect refused"));
        var client = CreateClient(handler);

        var ok = await client.VerifyLinkAsync(Guid.NewGuid(), "issue", Guid.NewGuid());

        Assert.False(ok);
    }

    // ── GetMetadataAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetMetadata_Success_PopulatesFileName()
    {
        var docId = Guid.NewGuid();
        using var handler = RespondJson(HttpStatusCode.OK, new
        {
            id = docId,
            parentFolderId = (Guid?)null,
            name = "design-spec.md",
            contentHash = "abc123",
            title = "Design Spec",
            content = "# hello",
            createdAt = DateTime.UtcNow
        });
        var client = CreateClient(handler);

        var meta = await client.GetMetadataAsync(docId);

        Assert.NotNull(meta);
        Assert.Equal(docId, meta!.DocumentId);
        Assert.Equal("design-spec.md", meta.FileName);
        // ContentType / SizeBytes are not yet exposed by the andy-docs
        // DocumentDto contract — placeholders today.
        Assert.Equal(string.Empty, meta.ContentType);
        Assert.Null(meta.SizeBytes);
    }

    [Fact]
    public async Task GetMetadata_NullName_FallsBackToEmptyFileName()
    {
        var docId = Guid.NewGuid();
        using var handler = RespondJson(HttpStatusCode.OK, new
        {
            id = docId,
            parentFolderId = (Guid?)null,
            name = (string?)null,
            contentHash = (string?)null,
            title = (string?)null,
            content = (string?)null,
            createdAt = DateTime.UtcNow
        });
        var client = CreateClient(handler);

        var meta = await client.GetMetadataAsync(docId);

        Assert.NotNull(meta);
        Assert.Equal(string.Empty, meta!.FileName);
    }

    [Fact]
    public async Task GetMetadata_NotFound_ReturnsNull()
    {
        using var handler = RespondStatus(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var meta = await client.GetMetadataAsync(Guid.NewGuid());

        Assert.Null(meta);
    }

    [Fact]
    public async Task GetMetadata_ServerError_ReturnsNull()
    {
        using var handler = RespondStatus(HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        var meta = await client.GetMetadataAsync(Guid.NewGuid());

        Assert.Null(meta);
    }

    [Fact]
    public async Task GetMetadata_NetworkException_ReturnsNull()
    {
        using var handler = new ThrowingHandler(new HttpRequestException("connect refused"));
        var client = CreateClient(handler);

        var meta = await client.GetMetadataAsync(Guid.NewGuid());

        Assert.Null(meta);
    }

    // Helpers — mirrors the StubHandler pattern used by other adapter
    // tests in this assembly (CodeIndexClientTests, BacklogAiServiceTests).

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
            _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));

        protected override void Dispose(bool disposing)
        {
            // Tests own response lifecycle.
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
