// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Security.Claims;
using Andy.Issues.Api.Auth;
using Xunit;

namespace Andy.Issues.Tests.Unit.Auth;

// Covers the claim-chain resolution and the throw-on-empty behavior
// introduced for issue #65 (no more silent "dev-user" fallback).
public class UserIdExtensionsTests
{
    [Fact]
    public void RequireUserId_PrefersSubClaim()
    {
        var principal = PrincipalWith(("sub", "abc-123"), (ClaimTypes.NameIdentifier, "nid"), (ClaimTypes.Name, "nm"));
        Assert.Equal("abc-123", principal.RequireUserId());
    }

    [Fact]
    public void RequireUserId_FallsBackToNameIdentifier()
    {
        var principal = PrincipalWith((ClaimTypes.NameIdentifier, "nid"), (ClaimTypes.Name, "nm"));
        Assert.Equal("nid", principal.RequireUserId());
    }

    [Fact]
    public void RequireUserId_FallsBackToIdentityName()
    {
        // No sub, no NameIdentifier — just Identity.Name.
        var identity = new ClaimsIdentity(authenticationType: "test");
        var principal = new ClaimsPrincipal(identity);
        // ClaimsIdentity derives Name from the NameClaimType, default
        // "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name".
        identity.AddClaim(new Claim(identity.NameClaimType, "fallback-name"));
        Assert.Equal("fallback-name", principal.RequireUserId());
    }

    [Fact]
    public void RequireUserId_NoClaims_Throws()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var ex = Assert.Throws<UnauthorizedAccessException>(() => principal.RequireUserId());
        Assert.Contains("No user id claim", ex.Message);
    }

    [Fact]
    public void RequireUserId_EmptyStringClaim_Throws()
    {
        // Defensive: a claim present but empty should not silently
        // attribute writes to the empty-string user.
        var principal = PrincipalWith(("sub", ""));
        Assert.Throws<UnauthorizedAccessException>(() => principal.RequireUserId());
    }

    private static ClaimsPrincipal PrincipalWith(params (string type, string value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.type, c.value)),
            authenticationType: "test");
        return new ClaimsPrincipal(identity);
    }
}
