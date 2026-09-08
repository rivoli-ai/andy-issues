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

public record SandboxSummaryDto(
    Guid Id, Guid RepositoryId, string RepositoryName, string Status,
    DateTimeOffset CreatedAt, string? IdeEndpoint, string? VncEndpoint, string Purpose,
    string Branch, string ContainerId);
public record SandboxCapacityDto(int Current, int Max, int TenantMax);
public record MySandboxesDto(IReadOnlyList<SandboxSummaryDto> Items, SandboxCapacityDto Capacity);
public record SandboxCloseFailureDto(Guid Id, string Reason);
public record CloseMySandboxesDto(IReadOnlyList<Guid> Destroyed, IReadOnlyList<SandboxCloseFailureDto> Failed);

public sealed class SandboxCapacityExceededException(int max)
    : InvalidOperationException($"Sandbox capacity reached ({max}). Close a sandbox before creating another.")
{
    public int Max { get; } = max;
}
