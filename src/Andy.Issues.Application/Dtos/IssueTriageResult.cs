// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

public enum IssueTriageOutcome
{
    Updated = 0,
    NotFound = 1,
    InvalidTransition = 2
}

public record IssueTriageResult(
    IssueTriageOutcome Outcome,
    IssueDto? Issue,
    string? Error)
{
    public static IssueTriageResult Ok(IssueDto issue) =>
        new(IssueTriageOutcome.Updated, issue, null);

    public static IssueTriageResult NotFound() =>
        new(IssueTriageOutcome.NotFound, null, null);

    public static IssueTriageResult InvalidTransition(string message) =>
        new(IssueTriageOutcome.InvalidTransition, null, message);
}
