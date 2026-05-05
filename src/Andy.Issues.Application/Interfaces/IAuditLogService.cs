// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Interfaces;

public interface IAuditLogService
{
    // Appends a row to the audit table in the same scope's DbContext.
    // The caller is expected to control the unit of work — the service
    // does not call SaveChanges so callers can batch the audit row with
    // the change it audits.
    Task LogAsync(
        string userId,
        string action,
        string resourceType,
        string resourceId,
        string? details = null,
        CancellationToken ct = default);
}
