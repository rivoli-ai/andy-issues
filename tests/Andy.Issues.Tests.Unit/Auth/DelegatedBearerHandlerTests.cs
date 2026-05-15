// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Headers;
using Andy.Auth.M2MClient;
using Andy.Issues.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Auth;

/// <summary>
/// Tests for the OBO-preferring bearer handler on andy-issues's
/// outbound clients. Drives Epic IDP (rivoli-ai/conductor#1246).
/// </summary>
public sealed class DelegatedBearerHandlerTests
{
    private const string Audience = "urn:andy-code-index-api";

    [Fact]
    public async Task SendAsync_WithInboundUserBearer_StampsObOToken()
    {
        var stub = new StubInner();
        var delegated = new StubDelegated("obo-jwt");
        var m2m = new StubM2M("m2m-jwt");
        var ctx = HttpContextWithUserBearer("user.jwt.signature");

        var handler = new DelegatedBearerHandler(
            delegated, m2m, ctx, Audience,
            NullLogger<DelegatedBearerHandler>.Instance)
        { InnerHandler = stub };

        await new HttpClient(handler).GetAsync("http://test/foo");

        Assert.Equal(new AuthenticationHeaderValue("Bearer", "obo-jwt"),
            stub.LastAuthorization);
        Assert.Equal("user.jwt.signature", delegated.LastSubjectToken);
        Assert.Equal(Audience, delegated.LastAudience);
        Assert.Equal(0, m2m.CallCount);
    }

    [Fact]
    public async Task SendAsync_WithoutHttpContext_FallsBackToM2M()
    {
        var stub = new StubInner();
        var delegated = new StubDelegated("obo-jwt");
        var m2m = new StubM2M("m2m-jwt");
        var ctx = new HttpContextAccessor();

        var handler = new DelegatedBearerHandler(
            delegated, m2m, ctx, Audience,
            NullLogger<DelegatedBearerHandler>.Instance)
        { InnerHandler = stub };

        await new HttpClient(handler).GetAsync("http://test/foo");

        Assert.Equal(new AuthenticationHeaderValue("Bearer", "m2m-jwt"),
            stub.LastAuthorization);
        Assert.Null(delegated.LastSubjectToken);
        Assert.Equal(1, m2m.CallCount);
    }

    [Fact]
    public async Task SendAsync_WhenObOFails_FallsBackToM2M()
    {
        var stub = new StubInner();
        var delegated = new ThrowingDelegated(new ServiceTokenException("policy denied"));
        var m2m = new StubM2M("m2m-jwt");
        var ctx = HttpContextWithUserBearer("user.jwt.signature");

        var handler = new DelegatedBearerHandler(
            delegated, m2m, ctx, Audience,
            NullLogger<DelegatedBearerHandler>.Instance)
        { InnerHandler = stub };

        await new HttpClient(handler).GetAsync("http://test/foo");

        Assert.Equal(new AuthenticationHeaderValue("Bearer", "m2m-jwt"),
            stub.LastAuthorization);
        Assert.Equal(1, m2m.CallCount);
    }

    [Fact]
    public async Task SendAsync_PreservesPreSetAuthorizationHeader()
    {
        var stub = new StubInner();
        var delegated = new StubDelegated("obo-jwt");
        var m2m = new StubM2M("m2m-jwt");
        var ctx = HttpContextWithUserBearer("user.jwt.signature");

        var handler = new DelegatedBearerHandler(
            delegated, m2m, ctx, Audience,
            NullLogger<DelegatedBearerHandler>.Instance)
        { InnerHandler = stub };

        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://test/foo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "pre-set");
        await client.SendAsync(request);

        Assert.Equal(new AuthenticationHeaderValue("Bearer", "pre-set"),
            stub.LastAuthorization);
        Assert.Null(delegated.LastSubjectToken);
        Assert.Equal(0, m2m.CallCount);
    }

    // ----- helpers -----

    private static HttpContextAccessor HttpContextWithUserBearer(string jwt)
    {
        var c = new DefaultHttpContext();
        c.Request.Headers.Authorization = $"Bearer {jwt}";
        return new HttpContextAccessor { HttpContext = c };
    }

    private sealed class StubInner : HttpMessageHandler
    {
        public AuthenticationHeaderValue? LastAuthorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastAuthorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class StubDelegated : IDelegatedTokenProvider
    {
        private readonly string _token;
        public string? LastSubjectToken { get; private set; }
        public string? LastAudience { get; private set; }
        public StubDelegated(string token) { _token = token; }
        public Task<string> GetTokenOnBehalfOfAsync(string subjectToken, string audience, CancellationToken ct = default)
        {
            LastSubjectToken = subjectToken; LastAudience = audience;
            return Task.FromResult(_token);
        }
    }

    private sealed class ThrowingDelegated : IDelegatedTokenProvider
    {
        private readonly Exception _ex;
        public ThrowingDelegated(Exception ex) { _ex = ex; }
        public Task<string> GetTokenOnBehalfOfAsync(string subjectToken, string audience, CancellationToken ct = default)
            => Task.FromException<string>(_ex);
    }

    private sealed class StubM2M : IServiceTokenProvider
    {
        private readonly string _token;
        public int CallCount { get; private set; }
        public StubM2M(string token) { _token = token; }
        public Task<string> GetTokenAsync(CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(_token);
        }
    }
}
