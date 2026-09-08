// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Infrastructure.Services;

/// <summary>Serializes capacity-changing requests within this service process.</summary>
public sealed class SandboxCapacityLock
{
    // Fixed stripes keep memory bounded while serializing every operation by one owner.
    private readonly SemaphoreSlim[] _stripes = Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    public static SandboxCapacityLock Shared { get; } = new();
    public async Task<IDisposable> AcquireAsync(string userId, CancellationToken ct)
    {
        var gate = _stripes[(uint)StringComparer.Ordinal.GetHashCode(userId) % (uint)_stripes.Length];
        await gate.WaitAsync(ct);
        return new Lease(gate);
    }
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
