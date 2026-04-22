// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Application.Interfaces;

/// <summary>
/// Allocates the next short-display-id sequence number for a given
/// <see cref="BacklogEntityType"/>. Implementations must be atomic
/// and monotonic under concurrent load — the callers rely on
/// "allocate → stamp entity.Seq → SaveChanges" producing no gaps and
/// no duplicates even with multiple parallel inserts. See AH1.
/// </summary>
public interface IBacklogSequenceAllocator
{
    /// <summary>
    /// Reserves and returns the next sequence number for
    /// <paramref name="type"/>. Starts at 1 for each type. Intended
    /// to be called inside the same transaction as the insert that
    /// consumes the returned value.
    /// </summary>
    Task<long> AllocateAsync(BacklogEntityType type, CancellationToken ct = default);
}
