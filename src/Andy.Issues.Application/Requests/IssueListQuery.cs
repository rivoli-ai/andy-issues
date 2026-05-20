// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Application.Requests;

// #187 — parsed shape of `GET /api/issues` query parameters. The
// controller binds the raw strings (`state`, `assignee`) and calls
// <see cref="IssueListQueryParser.TryParse"/> which validates each
// piece and returns this typed struct. Keeping the parser in the
// Application layer keeps the wire-format details out of the
// Infrastructure (EF) query method.
public sealed record IssueListQuery(
    IReadOnlyList<TriageState>? States,
    AssigneeFilter Assignee,
    Guid? RepositoryId,
    int Limit,
    string? Cursor);

// #187 — the three semantics from the issue spec:
//   `assignee=none`     → matches NULL assignee.
//   `assignee=me`       → matches the authenticated principal id.
//   `assignee=<id>`     → matches that specific user id.
//   (omitted)           → no assignee filter.
//
// `me` is resolved by the controller from the request principal before
// the query reaches the repository; the application layer sees a
// concrete user id in <see cref="UserId"/> when <see cref="Kind"/> is
// <see cref="AssigneeFilterKind.SpecificUser"/>.
public enum AssigneeFilterKind
{
    Unfiltered = 0,
    None = 1,
    SpecificUser = 2,
}

public sealed record AssigneeFilter(AssigneeFilterKind Kind, string? UserId)
{
    public static AssigneeFilter Unfiltered { get; } = new(AssigneeFilterKind.Unfiltered, null);
    public static AssigneeFilter None { get; } = new(AssigneeFilterKind.None, null);
    public static AssigneeFilter ForUser(string userId) => new(AssigneeFilterKind.SpecificUser, userId);
}
