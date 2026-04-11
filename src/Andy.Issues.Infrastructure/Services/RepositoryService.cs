// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Infrastructure.Services;

public class RepositoryService : IRepositoryService
{
    private readonly AppDbContext _db;
    private readonly IRepositoryAccessGuard _guard;

    public RepositoryService(AppDbContext db, IRepositoryAccessGuard guard)
    {
        _db = db;
        _guard = guard;
    }

    public async Task<PagedResult<RepositoryDto>> ListAsync(
        string userId,
        RepositoryScope scope,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        IQueryable<Repository> query = _db.Repositories.AsNoTracking();

        query = scope switch
        {
            RepositoryScope.Mine => query.Where(r => r.OwnerUserId == userId),
            RepositoryScope.Shared => query
                .Where(r => r.OwnerUserId != userId
                    && r.Shares.Any(s => s.SharedWithUserId == userId)),
            RepositoryScope.All => query.Where(r => r.OwnerUserId == userId
                || r.Shares.Any(s => s.SharedWithUserId == userId)),
            _ => query.Where(r => r.OwnerUserId == userId)
        };

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<RepositoryDto>(
            items.Select(r => r.ToDto()).ToList(),
            page,
            pageSize,
            total);
    }

    public async Task<RepositoryDto?> GetAsync(Guid id, string userId, CancellationToken ct = default)
    {
        if (!await _guard.CanViewAsync(id, userId, ct))
            return null;

        var repo = await _db.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        return repo?.ToDto();
    }

    public async Task<bool> DeleteAsync(Guid id, string userId, CancellationToken ct = default)
    {
        if (!await _guard.IsOwnerAsync(id, userId, ct))
            return false;

        var repo = await _db.Repositories
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (repo is null) return false;

        // Backlog (epics → features → stories), shares, and sandbox projection
        // rows cascade via EF relationships configured in AppDbContext. Live
        // container destruction against andy-containers is wired up in
        // Story 4.1 when SandboxService exists.
        _db.Repositories.Remove(repo);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
