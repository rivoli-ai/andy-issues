// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

// #101 — bulk-delete outcome. Partial success is allowed: callers
// receive per-id details for both successes (by kind) and failures.
//
// `Reason` is a stable string ("NotFound" | "Forbidden") — kept as a
// string rather than an enum so the wire contract carries a
// human-readable code without a separate enum lookup. Names match
// what the spec calls out.

public record BulkDeleteResult(
    BulkDeleteSuccess Deleted,
    IReadOnlyList<BulkDeleteFailure> Failed);

public record BulkDeleteSuccess(
    IReadOnlyList<Guid> Epics,
    IReadOnlyList<Guid> Features,
    IReadOnlyList<Guid> Stories);

public record BulkDeleteFailure(
    Guid Id,
    string Kind,    // "epic" | "feature" | "story"
    string Reason); // "NotFound" | "Forbidden"
