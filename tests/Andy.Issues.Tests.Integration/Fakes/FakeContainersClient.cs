// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Integration.Fakes;

public class FakeContainersClient : IContainersClient
{
    private readonly ConcurrentDictionary<string, ContainerInfo> _containers = new();
    private int _nextId;

    public IReadOnlyDictionary<string, ContainerInfo> Containers => _containers;

    public List<string> DestroyCalls { get; } = new();
    public List<(string name, string templateCode, IReadOnlyDictionary<string, string>? environmentVariables)> CreateCalls { get; } = new();
    public List<(string containerId, string command)> ExecCalls { get; } = new();
    public List<HeadlessRunRequest> HeadlessRunCalls { get; } = new();

    /// <summary>
    /// What <see cref="RunHeadlessAsync"/> returns. Defaults to a
    /// fresh <see cref="HeadlessRunResponse"/> with a generated id —
    /// tests asserting the failure path can set this to null.
    /// </summary>
    public HeadlessRunResponse? HeadlessRunResult { get; set; } =
        new HeadlessRunResponse(Guid.NewGuid());

    /// <summary>
    /// When non-null, <see cref="RunHeadlessAsync"/> throws this
    /// exception — simulates network/HTTP failures from
    /// andy-containers.
    /// </summary>
    public Exception? HeadlessRunException { get; set; }

    public void Reset()
    {
        _containers.Clear();
        DestroyCalls.Clear();
        CreateCalls.Clear();
        ExecCalls.Clear();
        HeadlessRunCalls.Clear();
        HeadlessRunResult = new HeadlessRunResponse(Guid.NewGuid());
        HeadlessRunException = null;
        _nextId = 0;
    }

    public void SetStatus(string containerId, string status)
    {
        if (_containers.TryGetValue(containerId, out var existing))
            _containers[containerId] = existing with { Status = status };
    }

    public Task<ContainerInfo> CreateContainerAsync(
        string name,
        string templateCode,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken ct = default)
    {
        CreateCalls.Add((name, templateCode, environmentVariables));
        var id = $"ctr-{Interlocked.Increment(ref _nextId):x8}";
        var info = new ContainerInfo(id, name, "Creating", IdeEndpoint: null, VncEndpoint: null);
        _containers[id] = info;
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
        _containers.TryRemove(containerId, out _);
        return Task.CompletedTask;
    }

    public Task<ContainerConnectionInfo?> GetConnectionInfoAsync(string containerId, CancellationToken ct = default)
    {
        if (!_containers.ContainsKey(containerId))
            return Task.FromResult<ContainerConnectionInfo?>(null);
        return Task.FromResult<ContainerConnectionInfo?>(new ContainerConnectionInfo(
            "10.0.0.1", "ssh://10.0.0.1:22", "https://ide/1", "vnc://1", null));
    }

    public Task<ContainerExecResult> ExecAsync(string containerId, string command, CancellationToken ct = default)
    {
        ExecCalls.Add((containerId, command));
        return Task.FromResult(new ContainerExecResult(0, "ok", null));
    }

    public Task<HeadlessRunResponse?> RunHeadlessAsync(
        HeadlessRunRequest request, CancellationToken ct = default)
    {
        HeadlessRunCalls.Add(request);
        if (HeadlessRunException is not null)
            throw HeadlessRunException;
        return Task.FromResult(HeadlessRunResult);
    }
}
