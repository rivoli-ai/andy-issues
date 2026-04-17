// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel.DataAnnotations;

namespace Andy.Issues.Application.Requests;

public record CreateLlmSettingRequest(
    [Required][StringLength(100, MinimumLength = 1)] string Name,
    [Required] string Provider,
    [Required] string ApiKey,
    [Required][StringLength(200, MinimumLength = 1)] string Model,
    string? BaseUrl,
    bool? IsDefault);

public record UpdateLlmSettingRequest(
    [StringLength(100, MinimumLength = 1)] string? Name,
    string? Provider,
    string? ApiKey,
    [StringLength(200, MinimumLength = 1)] string? Model,
    string? BaseUrl,
    bool? IsDefault);
