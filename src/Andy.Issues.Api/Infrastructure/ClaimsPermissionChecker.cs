// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Andy.Issues.Api.Infrastructure;

/// <summary>
/// Claim-backed implementation of <see cref="IPermissionChecker"/>. Looks for a
/// <c>permission</c> claim whose value matches the requested permission string
/// (e.g. <c>mcp:admin</c>). In auth-bypass mode (when <c>AndyAuth:Authority</c> is
/// empty) the checker returns <c>true</c> unconditionally so dev and Conductor
/// scenarios that rely on the open auth path still work end-to-end.
/// </summary>
/// <remarks>
/// Epic 8 will replace this with a thin adapter over <c>Andy.Rbac.Client</c>;
/// nothing in the service layer should have to change.
/// </remarks>
public class ClaimsPermissionChecker : IPermissionChecker
{
    private readonly IHttpContextAccessor _accessor;
    private readonly bool _authBypass;

    public ClaimsPermissionChecker(IHttpContextAccessor accessor, IConfiguration config)
    {
        _accessor = accessor;
        _authBypass = string.IsNullOrEmpty(config["AndyAuth:Authority"]);
    }

    public Task<bool> HasPermissionAsync(string userId, string permission, CancellationToken ct = default)
    {
        if (_authBypass) return Task.FromResult(true);

        var user = _accessor.HttpContext?.User;
        if (user is null) return Task.FromResult(false);

        var hasClaim = user.HasClaim(c =>
            c.Type == "permission" && string.Equals(c.Value, permission, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(hasClaim);
    }
}
