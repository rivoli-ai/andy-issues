// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Unit.Services;

public class FakeContainersClient : IContainersClient
{
    private readonly ConcurrentDictionary<string, ContainerInfo> _containers = new();
    private int _nextId = 1;

    public IReadOnlyDictionary<string, ContainerInfo> Containers => _containers;

    public List<string> DestroyCalls { get; } = new();
    public List<(string name, string templateCode, IReadOnlyDictionary<string, string>? environmentVariables)> CreateCalls { get; } = new();
    public Func<string, string, ContainerInfo>? CreateOverride { get; set; }
    public Exception? ThrowOnCreate { get; set; }
    public Exception? ThrowOnDestroy { get; set; }

    public void SeedContainer(string id, string name, string status, string? ide = null, string? vnc = null)
    {
        _containers[id] = new ContainerInfo(id, name, status, ide, vnc);
    }

    public void RemoveContainer(string id) => _containers.TryRemove(id, out _);

    public Task<ContainerInfo> CreateContainerAsync(
        string name,
        string templateCode,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken ct = default)
    {
        CreateCalls.Add((name, templateCode, environmentVariables));
        if (ThrowOnCreate is not null) throw ThrowOnCreate;

        var info = CreateOverride?.Invoke(name, templateCode)
            ?? new ContainerInfo(
                Id: $"ctr-{Interlocked.Increment(ref _nextId)}",
                Name: name,
                Status: "Creating",
                IdeEndpoint: null,
                VncEndpoint: null);
        _containers[info.Id] = info;
        return Task.FromResult(info);
    }

    public Task<ContainerInfo?> GetContainerAsync(string containerId, CancellationToken ct = default)
    {
        _containers.TryGetValue(containerId, out var info);
        return Task.FromResult<ContainerInfo?>(info);
    }

    public Task DestroyContainerAsync(string containerId, CancellationToken ct = default)
    {
        DestroyCalls.Add(containerId);
        if (ThrowOnDestroy is not null) throw ThrowOnDestroy;
        _containers.TryRemove(containerId, out _);
        return Task.CompletedTask;
    }

    public Task<ContainerConnectionInfo?> GetConnectionInfoAsync(string containerId, CancellationToken ct = default)
    {
        if (!_containers.TryGetValue(containerId, out var info))
            return Task.FromResult<ContainerConnectionInfo?>(null);
        return Task.FromResult<ContainerConnectionInfo?>(new ContainerConnectionInfo(
            IpAddress: "10.0.0.1",
            SshEndpoint: "ssh://10.0.0.1:22",
            IdeEndpoint: info.IdeEndpoint,
            VncEndpoint: info.VncEndpoint,
            PortMappings: null));
    }

    private Func<string, string, ContainerExecResult>? _execOverride;
    public List<(string containerId, string command)> ExecCalls { get; } = new();

    public void SetExec(Func<string, string, ContainerExecResult> handler)
    {
        _execOverride = handler;
    }

    public Task<ContainerExecResult> ExecAsync(string containerId, string command, CancellationToken ct = default)
    {
        ExecCalls.Add((containerId, command));
        var result = _execOverride?.Invoke(containerId, command)
            ?? new ContainerExecResult(0, $"stdout: {command}", null);
        return Task.FromResult(result);
    }
}
