// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Infrastructure.Services;

/// <summary>
/// Counter-row <see cref="IBacklogSequenceAllocator"/> backed by the
/// <c>backlog_sequences</c> table. Works uniformly on Postgres and
/// SQLite (3.35+) via <c>UPDATE ... RETURNING</c>, which atomically
/// increments the counter and returns the previous value in a single
/// statement — no read/check/write race. The ambient transaction
/// opened by the caller keeps the allocation and its owning insert
/// atomic.
/// </summary>
/// <remarks>
/// Callers MUST allocate inside their own transaction so a failed
/// downstream insert rolls the counter bump back. Otherwise a
/// rollback-only-the-insert produces a gap in the sequence.
/// </remarks>
public sealed class BacklogSequenceAllocator : IBacklogSequenceAllocator
{
    private readonly AppDbContext _db;

    public BacklogSequenceAllocator(AppDbContext db)
    {
        _db = db;
    }

    public async Task<long> AllocateAsync(
        BacklogEntityType type,
        CancellationToken ct = default)
    {
        int typeValue = (int)type;

        // Atomic read-modify-write via RETURNING. Both Npgsql and
        // Microsoft.Data.Sqlite (SQLite 3.35+) support this syntax.
        // We use `UPDATE ... RETURNING next_seq - 1` so the caller
        // receives the value it should stamp onto the entity while
        // the row's NextSeq is already positioned for the following
        // allocation.
        FormattableString sql =
            $"UPDATE backlog_sequences SET next_seq = next_seq + 1 WHERE entity_type = {typeValue} RETURNING next_seq - 1";

        // Ensure any tracked but unsaved BacklogSequence row is
        // flushed before the raw update; otherwise the UPDATE would
        // see a stale value.
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        List<long> allocated = await _db.Database
            .SqlQuery<long>(sql)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (allocated.Count == 1)
        {
            return allocated[0];
        }

        // Row didn't exist — lazy seed with value 1 (first allocation)
        // and bump to 2 for the next caller. The migration inserts
        // all three expected rows on first-apply; this branch only
        // fires if the table was truncated or a new entity type is
        // added without a paired migration.
        BacklogSequence seed = new() { EntityType = type, NextSeq = 2 };
        _db.BacklogSequences.Add(seed);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return 1;
    }
}
