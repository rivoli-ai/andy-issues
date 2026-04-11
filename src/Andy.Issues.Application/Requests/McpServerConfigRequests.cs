// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel.DataAnnotations;

namespace Andy.Issues.Application.Requests;

public record CreateMcpServerConfigRequest(
    [Required] string Name,
    string? Description,
    [Required] string Type,
    string? Command,
    string? ArgumentsJson,
    string? EnvironmentJson,
    string? Url,
    string? HeadersJson,
    bool? IsShared);

public record UpdateMcpServerConfigRequest(
    string? Name,
    string? Description,
    bool? Enabled,
    string? Command,
    string? ArgumentsJson,
    string? EnvironmentJson,
    string? Url,
    string? HeadersJson);
