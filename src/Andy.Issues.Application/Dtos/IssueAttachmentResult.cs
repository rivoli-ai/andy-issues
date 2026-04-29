// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

// Z8 — attach outcome. Mirrors IssueTriageResult: a state-bearing
// record so the controller maps to HTTP cleanly. `LinkRejected`
// covers both "link doesn't exist in andy-docs" (real verification)
// and "malformed UUIDs" (stub rejection).
public enum IssueAttachmentOutcome
{
    Attached = 0,
    NotFound = 1,
    InvalidState = 2,
    LinkRejected = 3,
    AlreadyAttached = 4
}

public record IssueAttachmentResult(
    IssueAttachmentOutcome Outcome,
    IssueAttachmentDto? Attachment,
    string? Error)
{
    public static IssueAttachmentResult Ok(IssueAttachmentDto dto) =>
        new(IssueAttachmentOutcome.Attached, dto, null);

    public static IssueAttachmentResult AlreadyAttached(IssueAttachmentDto dto) =>
        new(IssueAttachmentOutcome.AlreadyAttached, dto, null);

    public static IssueAttachmentResult NotFound() =>
        new(IssueAttachmentOutcome.NotFound, null, null);

    public static IssueAttachmentResult InvalidState(string message) =>
        new(IssueAttachmentOutcome.InvalidState, null, message);

    public static IssueAttachmentResult LinkRejected(string message) =>
        new(IssueAttachmentOutcome.LinkRejected, null, message);
}
