// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel.DataAnnotations;

namespace Andy.Issues.Application.Requests;

public record CreateLinkedProviderRequest(
    [Required] string Provider,
    [Required] string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string? AccountLogin);

public record LinkPatRequest(
    [Required] string Provider,
    [Required] string Pat);
