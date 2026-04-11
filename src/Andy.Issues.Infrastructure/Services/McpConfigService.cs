// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Infrastructure.Services;

public class McpConfigService : IMcpConfigService
{
    private readonly AppDbContext _db;

    public McpConfigService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<McpServerConfigFull>> GetEnabledForUserAsync(
        string userId,
        CancellationToken ct = default)
    {
        var rows = await _db.McpServerConfigs
            .AsNoTracking()
            .Where(c => c.Enabled && (c.OwnerUserId == userId || c.IsShared))
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        return rows
            .Select(c => new McpServerConfigFull(
                c.Id,
                c.Name,
                c.Description,
                c.Type.ToString(),
                c.Enabled,
                c.Command,
                c.ArgumentsJson,
                c.EnvironmentJson,
                c.Url,
                c.HeadersJson))
            .ToList();
    }
}
