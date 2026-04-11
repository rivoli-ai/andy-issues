// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net.Http.Headers;

namespace Andy.Issues.Api.Infrastructure;

/// <summary>
/// Delegating handler that copies the caller's Bearer token from the ambient HttpContext
/// onto outgoing requests. Used by the andy-containers client so every call runs under
/// the identity of whoever is hitting andy-issues, without plumbing tokens through the
/// service layer.
/// </summary>
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
