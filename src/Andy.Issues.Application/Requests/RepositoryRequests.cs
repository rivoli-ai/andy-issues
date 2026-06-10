// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel.DataAnnotations;

namespace Andy.Issues.Application.Requests;

public record CreateRepositoryRequest(
    [Required] string Name,
    string? Description,
    [Required] string Provider,
    [Required] string CloneUrl,
    string? DefaultBranch,
    string? ExternalId);

public record UpdateRepositoryRequest(
    string? Name,
    string? Description,
    string? DefaultBranch);

public record UpdateRepositoryLlmRequest(
    Guid? LlmSettingId);

public record UpdateRepositoryAzureIdentityRequest(
    [Required] string ClientId,
    [Required] string ClientSecret,
    [Required] string TenantId,
    string? SubscriptionId);

public record UpdateRepositoryAzurePatIdentityRequest(
    [Required] string Organization,
    [Required] string Project,
    [Required] string Pat);

public record ShareRepositoryRequest(
    [Required][EmailAddress] string Email);

public record SyncGitHubRepositoriesRequest(
    [Required] IReadOnlyList<string> RepoIds);

public record SyncAzureDevOpsRepositoriesRequest(
    [Required] string Organization,
    string? Project,
    [Required] IReadOnlyList<string> RepoIds);

/// <summary>
/// Body for <c>POST /api/repositories/{id}/recategorize</c>. The body
/// is optional — absent means <c>applyToGitHub = false</c> (classify
/// locally only).
/// </summary>
public record RecategorizeBacklogRequest(
    bool ApplyToGitHub = false);

public record CreatePullRequestRequest(
    [Required] Guid SandboxId,
    [Required] string Title,
    string? Description,
    [Required] string SourceBranch,
    [Required] string TargetBranch,
    Guid? StoryId);
