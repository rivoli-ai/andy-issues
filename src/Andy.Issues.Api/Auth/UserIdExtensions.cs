// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Security.Claims;

namespace Andy.Issues.Api.Auth;

// Centralized lookup of the authenticated user's id from a
// ClaimsPrincipal. Throws UnauthorizedAccessException when no
// identifier claim is present so the caller cannot silently attribute
// writes to a phantom fallback user (see issue #65).
//
// The three-claim chain mirrors what the previous inline GetUserId
// helpers did: Andy Auth emits `sub`, ASP.NET Core usually maps it to
// NameIdentifier, and a last-ditch Identity.Name covers test
// principals that only set that property.
public static class UserIdExtensions
{
    public static string RequireUserId(this ClaimsPrincipal user)
    {
        var id = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.Identity?.Name;

        if (string.IsNullOrEmpty(id))
        {
            throw new UnauthorizedAccessException(
                "No user id claim present on the authenticated principal. " +
                "Expected 'sub' or NameIdentifier. Check Andy Auth token configuration.");
        }

        return id;
    }
}
