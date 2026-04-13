// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Interfaces;

/// <summary>
/// Abstracts secret storage so the same domain field can hold either a raw
/// value (legacy / dev-mode) or a reference to an andy-settings encrypted
/// secret. Values prefixed with <c>secret::</c> are treated as references
/// and resolved via <see cref="IAndySettingsClient"/>; everything else is
/// returned as-is.
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// Resolves a stored value: if it's a <c>secret::</c> ref, fetches the
    /// real secret from andy-settings; otherwise returns the raw value.
    /// </summary>
    Task<string?> ResolveAsync(string? valueOrRef, CancellationToken ct = default);

    /// <summary>
    /// Stores a secret in andy-settings and returns the <c>secret::</c>
    /// reference to persist in the database column. When andy-settings is
    /// not configured, returns the raw value unchanged (dev-mode).
    /// </summary>
    Task<string> StoreAsync(string key, string value, CancellationToken ct = default);
}
