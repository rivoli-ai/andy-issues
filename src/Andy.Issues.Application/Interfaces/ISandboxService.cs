// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;

namespace Andy.Issues.Application.Interfaces;

public interface ISandboxService
{
    Task<SandboxDto?> CreateAsync(CreateSandboxRequest request, string userId, CancellationToken ct = default);

    Task<IReadOnlyList<SandboxDto>> ListAsync(string userId, CancellationToken ct = default);

    Task<SandboxDto?> GetAsync(Guid sandboxId, string userId, CancellationToken ct = default);

    Task<bool> DestroyAsync(Guid sandboxId, string userId, CancellationToken ct = default);

    Task<SandboxConnectionDto?> GetConnectionInfoAsync(Guid sandboxId, string userId, CancellationToken ct = default);
}
