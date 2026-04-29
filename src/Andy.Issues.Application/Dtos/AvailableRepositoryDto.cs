// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

// #99 — wire shape for a repository that exists at the upstream
// provider but has not yet been synced into andy-issues for the
// caller. The Conductor "Choose repos" modal reads this list and
// posts the user-selected `ExternalId` set back to the existing
// `sync-github` endpoint to import them.
public record AvailableRepositoryDto(
    string ExternalId,
    string Name,
    string FullName,
    string? Description,
    string CloneUrl,
    string DefaultBranch,
    string Provider);
