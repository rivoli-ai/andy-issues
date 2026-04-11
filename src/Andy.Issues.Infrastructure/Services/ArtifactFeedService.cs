// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Infrastructure.Services;

public class ArtifactFeedService : IArtifactFeedService
{
    private readonly AppDbContext _db;

    public ArtifactFeedService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ArtifactFeedConfigDto>> GetEnabledAsync(CancellationToken ct = default)
    {
        var rows = await _db.ArtifactFeedConfigs
            .AsNoTracking()
            .Where(f => f.Enabled)
            .OrderBy(f => f.Name)
            .ToListAsync(ct);
        return rows.Select(f => f.ToDto()).ToList();
    }
}
