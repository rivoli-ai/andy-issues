// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;

namespace Andy.Issues.Application.Interfaces;

public interface IArtifactFeedService
{
    Task<IReadOnlyList<ArtifactFeedConfigDto>> GetEnabledAsync(CancellationToken ct = default);
}
