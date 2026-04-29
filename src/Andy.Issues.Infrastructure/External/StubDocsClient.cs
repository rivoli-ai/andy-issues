// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.External;

// Z8 — placeholder IDocsClient until andy-docs Epic AJ ships. Accepts
// any well-formed (DocumentId, LinkId) pair and returns null metadata
// so the attachment list DTO carries placeholder values.
//
// When AJ lands, this class is replaced by a real HTTP client; the
// IDocsClient interface stays unchanged.
public sealed class StubDocsClient : IDocsClient
{
    private readonly ILogger<StubDocsClient> _logger;

    public StubDocsClient(ILogger<StubDocsClient> logger)
    {
        _logger = logger;
    }

    public Task<bool> VerifyLinkAsync(
        Guid linkId,
        string expectedTargetType,
        Guid expectedTargetId,
        CancellationToken ct = default)
    {
        // Reject obviously-malformed callers (Guid.Empty) but accept
        // anything else. Real verification lands with andy-docs AJ.
        var ok = linkId != Guid.Empty
            && !string.IsNullOrWhiteSpace(expectedTargetType)
            && expectedTargetId != Guid.Empty;

        if (!ok)
            _logger.LogDebug(
                "StubDocsClient.VerifyLinkAsync rejecting malformed link {LinkId} " +
                "(target={Target}/{TargetId})",
                linkId, expectedTargetType, expectedTargetId);

        return Task.FromResult(ok);
    }

    public Task<DocsMetadata?> GetMetadataAsync(Guid documentId, CancellationToken ct = default)
    {
        // Stub returns null — Conductor will hit a real andy-docs
        // when AJ is wired. For now, the attachment list shows the
        // DocumentId/LinkId pair without filename/MIME.
        return Task.FromResult<DocsMetadata?>(null);
    }
}
