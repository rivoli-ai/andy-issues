// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;

namespace Andy.Issues.Infrastructure.Services;

public class AuditLogService : IAuditLogService
{
    private readonly AppDbContext _db;

    public AuditLogService(AppDbContext db)
    {
        _db = db;
    }

    public Task LogAsync(
        string userId,
        string action,
        string resourceType,
        string resourceId,
        string? details = null,
        CancellationToken ct = default)
    {
        _db.AuditLog.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
            CreatedAt = DateTimeOffset.UtcNow
        });
        return Task.CompletedTask;
    }
}
