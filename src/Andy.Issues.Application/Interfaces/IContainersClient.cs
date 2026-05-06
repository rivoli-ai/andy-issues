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
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken ct = default);

    Task<ContainerInfo?> GetContainerAsync(string containerId, CancellationToken ct = default);

    Task DestroyContainerAsync(string containerId, CancellationToken ct = default);

    Task<ContainerConnectionInfo?> GetConnectionInfoAsync(string containerId, CancellationToken ct = default);

    Task<ContainerExecResult> ExecAsync(string containerId, string command, CancellationToken ct = default);

    /// <summary>
    /// Enqueues a headless agent run on andy-containers (Z2 — triage
    /// invocation). Returns the run id assigned by the upstream so the
    /// caller can persist it for later correlation. Returns null on
    /// non-2xx responses so callers can degrade gracefully — the
    /// dispatching code will leave the originating entity in a state
    /// that allows re-invocation.
    /// </summary>
    /// <remarks>
    /// Wire shape is match-ahead with andy-containers — the upstream
    /// endpoint <c>POST api/runs</c> is not yet implemented at the
    /// time of writing. The local fake exercises the wire in tests.
    /// </remarks>
    Task<HeadlessRunResponse?> RunHeadlessAsync(
        HeadlessRunRequest request,
        CancellationToken ct = default);
}

public sealed record HeadlessRunRequest(
    string AgentId,
    string? AgentVersion = null,
    Guid? IssueId = null,
    Guid? StoryId = null,
    string? TenantId = null,
    IReadOnlyList<string>? InputDocRefs = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null);

public sealed record HeadlessRunResponse(Guid RunId);

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
