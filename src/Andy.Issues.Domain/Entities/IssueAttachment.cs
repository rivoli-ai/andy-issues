// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Entities;

// Z8 — pin from an Issue to a document stored in andy-docs. The
// composite key (IssueId, LinkId) enforces one row per typed link;
// re-attaching the same DocumentLink is a no-op rather than a
// duplicate.
//
// Conductor uploads the file directly to andy-docs and POSTs the
// resulting `(DocumentId, LinkId)` here for pinning. andy-issues
// stores only the pointer per the artifacts-in-andy-docs decision —
// the file payload never lands in this database.
public class IssueAttachment
{
    public Guid IssueId { get; set; }

    // Pointer into andy-docs (AJ2). DocumentId is the content
    // reference; LinkId is the typed DocumentLink row that scopes the
    // reference to this Issue with role=input.
    public Guid DocumentId { get; set; }
    public Guid LinkId { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
