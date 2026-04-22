// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Domain.Entities;

/// <summary>
/// Counter row backing the short-display-id allocation for a single
/// <see cref="BacklogEntityType"/>. Exactly one row per type exists;
/// <c>NextSeq</c> is atomically incremented by the allocator inside
/// the same transaction as the insert it's being used for.
/// See AH1.
/// </summary>
/// <remarks>
/// Scoped globally for now — a tenant partition will be added in a
/// follow-up story once multi-tenant isolation lands with AL-auth.
/// The counter-row approach (rather than native Postgres sequences)
/// was chosen to work uniformly on Postgres and SQLite and to keep
/// the schema bounded.
/// </remarks>
public class BacklogSequence
{
    public BacklogEntityType EntityType { get; set; }

    public long NextSeq { get; set; }
}
