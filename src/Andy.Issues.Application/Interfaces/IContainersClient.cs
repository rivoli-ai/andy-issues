// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Interfaces;

/// <summary>
/// Thin abstraction over <c>Andy.Containers.Client.ContainersClient</c> exposing just the
/// container operations andy-issues needs. Lives in Application so services and tests can
/// depend on it without pulling a real HTTP client — the concrete implementation wraps the
/// sealed upstream client and is registered in Infrastructure.
/// </summary>
public interface IContainersClient
{
    Task<ContainerInfo> CreateContainerAsync(
        string name,
        string templateCode,
        CancellationToken ct = default);

    Task<ContainerInfo?> GetContainerAsync(string containerId, CancellationToken ct = default);

    Task DestroyContainerAsync(string containerId, CancellationToken ct = default);

    Task<ContainerConnectionInfo?> GetConnectionInfoAsync(string containerId, CancellationToken ct = default);

    Task<ContainerExecResult> ExecAsync(string containerId, string command, CancellationToken ct = default);
}

public record ContainerInfo(
    string Id,
    string Name,
    string Status,
    string? IdeEndpoint,
    string? VncEndpoint);

public record ContainerConnectionInfo(
    string? IpAddress,
    string? SshEndpoint,
    string? IdeEndpoint,
    string? VncEndpoint,
    IReadOnlyDictionary<string, int>? PortMappings);

public record ContainerExecResult(int ExitCode, string? StdOut, string? StdErr);
