// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net.Http.Headers;
using Andy.Auth.M2MClient;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Api.Infrastructure;

/// <summary>
/// HTTP message handler that stamps an OAuth bearer onto every outbound
/// request, preferring an RFC 8693 token-exchanged (on-behalf-of)
/// token when the current HTTP request carries a user JWT, and falling
/// back to a pure M2M client-credentials bearer otherwise.
///
/// Use this in place of <see cref="BearerForwardingHandler"/> on every
/// outbound client whose calls originate from a user-facing controller.
/// <see cref="BearerForwardingHandler"/> forwarded the user's raw JWT
/// unchanged (RFC 6749 Option A) — that works for synchronous,
/// single-audience, short-lifetime calls but breaks on lifetime gaps
/// (e.g. background paths with stored subjects), audience mismatches,
/// and leaves the calling service invisible in the audit trail. OBO
/// solves all three. See Epic IDP (rivoli-ai/conductor#1246).
///
/// Per-handler audience binding: each registered handler instance is
/// tied to a single downstream audience (e.g.
/// <c>urn:andy-code-index-api</c>).
/// </summary>
public sealed class DelegatedBearerHandler : DelegatingHandler
{
    private readonly IDelegatedTokenProvider _delegated;
    private readonly IServiceTokenProvider _m2m;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _audience;
    private readonly ILogger<DelegatedBearerHandler> _logger;

    public DelegatedBearerHandler(
        IDelegatedTokenProvider delegated,
        IServiceTokenProvider m2m,
        IHttpContextAccessor httpContextAccessor,
        string audience,
        ILogger<DelegatedBearerHandler> logger)
    {
        _delegated = delegated;
        _m2m = m2m;
        _httpContextAccessor = httpContextAccessor;
        _audience = audience;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var bearer = await ResolveBearerAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(bearer))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            }
        }
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ResolveBearerAsync(CancellationToken ct)
    {
        var userJwt = TryExtractInboundUserJwt();
        if (!string.IsNullOrWhiteSpace(userJwt))
        {
            try
            {
                _logger.LogDebug(
                    "DelegatedBearerHandler: using OBO bearer (audience={Audience})",
                    _audience);
                return await _delegated
                    .GetTokenOnBehalfOfAsync(userJwt, _audience, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceTokenException ex)
            {
                _logger.LogWarning(ex,
                    "OBO exchange failed for audience {Audience}; falling back to M2M.",
                    _audience);
            }
        }
        try
        {
            return await _m2m.GetTokenAsync(ct).ConfigureAwait(false);
        }
        catch (ServiceTokenException ex)
        {
            _logger.LogError(ex,
                "M2M bearer fetch failed for audience {Audience}; outbound call will be unauthenticated.",
                _audience);
            return null;
        }
    }

    private string? TryExtractInboundUserJwt()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null)
        {
            return null;
        }
        var header = ctx.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header.Substring(prefix.Length).Trim()
            : null;
    }
}
