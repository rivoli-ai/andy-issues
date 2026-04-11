// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using Andy.Containers.Client;
using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Infrastructure.External;

/// <summary>
/// Adapts the upstream sealed <see cref="ContainersClient"/> to the Application-layer
/// <see cref="IContainersClient"/> abstraction so services depend on something testable
/// and andy-issues stays out of the business of managing containers directly.
/// </summary>
public class AndyContainersClientAdapter : IContainersClient
{
    private readonly ContainersClient _inner;

    public AndyContainersClientAdapter(ContainersClient inner)
    {
        _inner = inner;
    }

    public async Task<ContainerInfo> CreateContainerAsync(
        string name,
        string templateCode,
        CancellationToken ct = default)
    {
        var dto = await _inner.CreateContainerAsync(name, templateCode, ct: ct);
        return ToInfo(dto);
    }

    public async Task<ContainerInfo?> GetContainerAsync(string containerId, CancellationToken ct = default)
    {
        try
        {
            var dto = await _inner.GetContainerAsync(containerId, ct);
            return ToInfo(dto);
        }
        catch (ContainersApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DestroyContainerAsync(string containerId, CancellationToken ct = default)
    {
        try
        {
            await _inner.DestroyContainerAsync(containerId, ct);
        }
        catch (ContainersApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone — treat as success so local state can be reconciled.
        }
    }

    public async Task<ContainerConnectionInfo?> GetConnectionInfoAsync(string containerId, CancellationToken ct = default)
    {
        try
        {
            var dto = await _inner.GetConnectionInfoAsync(containerId, ct);
            return new ContainerConnectionInfo(
                dto.IpAddress,
                dto.SshEndpoint,
                dto.IdeEndpoint,
                dto.VncEndpoint,
                dto.PortMappings);
        }
        catch (ContainersApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<ContainerExecResult> ExecAsync(string containerId, string command, CancellationToken ct = default)
    {
        var result = await _inner.ExecAsync(containerId, command, ct);
        return new ContainerExecResult(result.ExitCode, result.StdOut, result.StdErr);
    }

    private static ContainerInfo ToInfo(ContainersClient.ContainerDto dto) =>
        new(dto.Id, dto.Name, dto.Status, dto.IdeEndpoint, dto.VncEndpoint);
}
