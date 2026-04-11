// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Interfaces;

public interface IRepositoryAccessGuard
{
    Task<bool> CanViewAsync(Guid repositoryId, string userId, CancellationToken ct = default);
    Task<bool> IsOwnerAsync(Guid repositoryId, string userId, CancellationToken ct = default);
}
