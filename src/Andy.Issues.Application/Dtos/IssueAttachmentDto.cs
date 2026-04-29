// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

// Wire shape for an Issue attachment (Z8). FileName/ContentType/SizeBytes
// come from andy-docs metadata via IDocsClient.GetMetadataAsync; null
// when the stub is wired (until andy-docs Epic AJ ships).
public record IssueAttachmentDto(
    Guid IssueId,
    Guid DocumentId,
    Guid LinkId,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string? FileName,
    string? ContentType,
    long? SizeBytes);
