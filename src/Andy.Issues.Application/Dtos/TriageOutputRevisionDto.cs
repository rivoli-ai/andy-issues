// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.ValueTypes;

namespace Andy.Issues.Application.Dtos;

// Wire shape for a TriageOutputRevision row. AuthorKind is the
// stringified enum (Agent / Human) to match the rest of this codebase
// — the API uses JsonStringEnumConverter for response serialisation.
public record TriageOutputRevisionDto(
    Guid Id,
    Guid IssueId,
    string Author,
    string AuthorKind,
    TriageOutput TriageOutput,
    string? DiffSummary,
    DateTimeOffset CreatedAt);
