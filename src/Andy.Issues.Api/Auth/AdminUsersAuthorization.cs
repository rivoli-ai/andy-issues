// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Security.Claims;

namespace Andy.Issues.Api.Auth;

public static class AdminUsersAuthorization
{
    public const string Policy = "AdminUsersRead";
    // The registration schema uses service:resource:action. Accept the original issue spelling too.
    public const string Permission = "andy-issues:admin-users:read";
    public static bool CanRead(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true
        && user.HasClaim(c => c.Type == "permission" && c.Value is Permission or "andy-issues:admin:users:read");
}
