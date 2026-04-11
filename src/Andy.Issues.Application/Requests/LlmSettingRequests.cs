// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel.DataAnnotations;

namespace Andy.Issues.Application.Requests;

public record CreateLlmSettingRequest(
    [Required] string Name,
    [Required] string Provider,
    [Required] string ApiKey,
    [Required] string Model,
    string? BaseUrl,
    bool? IsDefault);

public record UpdateLlmSettingRequest(
    string? Name,
    string? ApiKey,
    string? Model,
    string? BaseUrl,
    bool? IsDefault);
