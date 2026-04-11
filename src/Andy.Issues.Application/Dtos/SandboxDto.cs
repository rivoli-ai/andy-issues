// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

public record SandboxDto(
    Guid Id,
    string ContainerId,
    Guid RepositoryId,
    string OwnerUserId,
    string Branch,
    string Status,
    string? IdeEndpoint,
    string? VncEndpoint,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public record SandboxConnectionDto(
    string? IdeEndpoint,
    string? VncEndpoint,
    string? SshEndpoint);
