// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Interfaces;

// Thin client into andy-docs (AJ2). Z8 uses two methods:
//   VerifyLinkAsync  — confirm a `(LinkId)` row exists in andy-docs
//                      with the expected target, before pinning.
//   GetMetadataAsync — fetch filename + MIME for the attachment list
//                      DTO so the Conductor UI can render without a
//                      second round trip.
//
// Both methods are stubbed today (StubDocsClient — accepts any
// well-formed UUID pair, returns null metadata) until andy-docs Epic
// AJ ships the canonical surface. Real implementations point at
// andy-docs over HTTP and surface the AJ contract.
public interface IDocsClient
{
    Task<bool> VerifyLinkAsync(
        Guid linkId,
        string expectedTargetType,
        Guid expectedTargetId,
        CancellationToken ct = default);

    Task<DocsMetadata?> GetMetadataAsync(Guid documentId, CancellationToken ct = default);
}

// Subset of andy-docs document metadata — only what andy-issues needs
// to render an attachment row. Real shape grows when AJ ships.
public record DocsMetadata(
    Guid DocumentId,
    string FileName,
    string ContentType,
    long? SizeBytes);
