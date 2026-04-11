// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Interfaces;

/// <summary>
/// Tiny seam for admin-style permission checks (<c>mcp:admin</c>, <c>artifact:admin</c>,
/// etc.) that today reads a claim off the current principal and tomorrow will delegate to
/// Andy RBAC (Epic 8). Service code and controllers depend on this abstraction so Epic 8
/// can swap the backing implementation without touching business logic.
/// </summary>
public interface IPermissionChecker
{
    /// <summary>
    /// Returns <c>true</c> if the current caller holds <paramref name="permission"/>. The
    /// implementation may or may not use <paramref name="userId"/>; it is passed in so
    /// future RBAC calls can target a specific subject without taking a dependency on
    /// <c>HttpContext</c> in the service layer.
    /// </summary>
    Task<bool> HasPermissionAsync(string userId, string permission, CancellationToken ct = default);
}
