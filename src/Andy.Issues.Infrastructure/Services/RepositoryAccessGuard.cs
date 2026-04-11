// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Infrastructure.Services;

public class RepositoryAccessGuard : IRepositoryAccessGuard
{
    private readonly AppDbContext _db;

    public RepositoryAccessGuard(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> CanViewAsync(Guid repositoryId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return false;

        return await _db.Repositories
            .AsNoTracking()
            .Where(r => r.Id == repositoryId)
            .Select(r => r.OwnerUserId == userId
                || r.Shares.Any(s => s.SharedWithUserId == userId))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> IsOwnerAsync(Guid repositoryId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return false;

        return await _db.Repositories
            .AsNoTracking()
            .AnyAsync(r => r.Id == repositoryId && r.OwnerUserId == userId, ct);
    }
}
