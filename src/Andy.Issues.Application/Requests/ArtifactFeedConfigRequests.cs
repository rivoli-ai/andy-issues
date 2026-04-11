// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel.DataAnnotations;

namespace Andy.Issues.Application.Requests;

public record CreateArtifactFeedConfigRequest(
    [Required] string Name,
    [Required] string Organization,
    [Required] string FeedName,
    string? Project,
    [Required] string Type);

public record UpdateArtifactFeedConfigRequest(
    string? Name,
    string? Project,
    bool? Enabled);
