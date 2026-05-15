// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net.Http.Headers;

namespace Andy.Issues.Api.Infrastructure;

/// <summary>
/// Delegating handler that copies the caller's Bearer token from the ambient HttpContext
/// onto outgoing requests. Used by the andy-containers client so every call runs under
/// the identity of whoever is hitting andy-issues, without plumbing tokens through the
/// service layer.
///
/// <para>
/// <strong>Deprecated</strong> in favour of <see cref="DelegatedBearerHandler"/> as of
/// Epic IDP (rivoli-ai/conductor#1246). This handler forwards the user's raw JWT
/// unchanged — that works for synchronous, single-audience, short-lifetime calls but
/// breaks on lifetime gaps (e.g. 7-day per-container proxy tokens), confused-deputy
/// risk (any service receiving the bearer can use it elsewhere), and audience
/// mismatches (the user's JWT was issued for the caller's audience, not the
/// downstream's). The OBO-aware <see cref="DelegatedBearerHandler"/> covers all three.
/// New call sites should prefer that handler; this file remains for emergency
/// fallback only.
/// </para>
/// </summary>
// NOTE: not [Obsolete] yet — three remaining call sites in Program.cs
// (AndySettings, AndyDocs, AndyContainers clients) still depend on it.
// Promote to [Obsolete] in a follow-up PR after those three are
// migrated to DelegatedBearerHandler too. Deprecation is documented
// above so anyone evaluating new usage gets the signal.
public class BearerForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _accessor;

    public BearerForwardingHandler(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var header = _accessor.HttpContext?.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(header) &&
            header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                header["Bearer ".Length..]);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
