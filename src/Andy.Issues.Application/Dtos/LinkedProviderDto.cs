// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

// Outbound shape: never exposes AccessToken or RefreshToken.
public record LinkedProviderDto(
    Guid Id,
    string Provider,
    string? AccountLogin,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
