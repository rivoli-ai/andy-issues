// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel.DataAnnotations;

namespace Andy.Issues.Application.Requests;

public record CreateSandboxRequest(
    [Required] Guid RepositoryId,
    [Required] string Branch,
    string? Resolution);

public record CreateSandboxPullRequestRequest(
    [Required] string Title,
    string? Description,
    Guid? StoryId);

public record CreatePullRequestFromSandboxRequest(
    [Required] Guid SandboxId,
    [Required] string Title,
    string? Description,
    [Required] string SourceBranch,
    [Required] string TargetBranch,
    Guid? StoryId);
