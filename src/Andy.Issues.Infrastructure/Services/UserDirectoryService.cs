// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Infrastructure.Services;

public class UserDirectoryService : IUserDirectory
{
    private readonly AppDbContext _db;

    public UserDirectoryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserRecord?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var normalized = email.Trim();
        var entry = await _db.UserDirectory
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Email == normalized, ct);
        return entry is null ? null : new UserRecord(entry.UserId, entry.Email, entry.DisplayName);
    }

    public async Task<UserRecord?> FindByUserIdAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;
        var entry = await _db.UserDirectory
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == userId, ct);
        return entry is null ? null : new UserRecord(entry.UserId, entry.Email, entry.DisplayName);
    }

    public async Task<IReadOnlyList<UserRecord>> SuggestAsync(
        string query,
        string excludeUserId,
        int limit,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0)
            return Array.Empty<UserRecord>();

        if (limit > 50) limit = 50;
        var q = query.Trim();

        var matches = await _db.UserDirectory
            .AsNoTracking()
            .Where(e => e.UserId != excludeUserId)
            .Where(e => EF.Functions.Like(e.Email, q + "%")
                || (e.DisplayName != null && EF.Functions.Like(e.DisplayName, q + "%")))
            .OrderBy(e => e.Email)
            .Take(limit)
            .ToListAsync(ct);

        return matches
            .Select(e => new UserRecord(e.UserId, e.Email, e.DisplayName))
            .ToList();
    }

    public async Task UpsertAsync(UserRecord record, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(record.UserId) || string.IsNullOrWhiteSpace(record.Email))
            return;

        var existing = await _db.UserDirectory
            .FirstOrDefaultAsync(e => e.UserId == record.UserId, ct);

        if (existing is null)
        {
            _db.UserDirectory.Add(new UserDirectoryEntry
            {
                Id = Guid.NewGuid(),
                UserId = record.UserId,
                Email = record.Email,
                DisplayName = record.DisplayName
            });
        }
        else
        {
            if (existing.Email != record.Email ||
                existing.DisplayName != record.DisplayName)
            {
                existing.Email = record.Email;
                existing.DisplayName = record.DisplayName;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
