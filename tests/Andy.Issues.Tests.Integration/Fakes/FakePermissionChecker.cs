// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Integration.Fakes;

/// <summary>
/// Toggleable permission checker for integration tests. Defaults to non-admin so the
/// authorization paths are exercised by default; individual tests flip to admin to cover
/// the shared/admin endpoints.
/// </summary>
public class FakePermissionChecker : IPermissionChecker
{
    private volatile bool _isAdmin;

    public void SetAdmin(bool isAdmin) => _isAdmin = isAdmin;

    public void Reset() => _isAdmin = false;

    public Task<bool> HasPermissionAsync(string userId, string permission, CancellationToken ct = default)
        => Task.FromResult(_isAdmin);
}
