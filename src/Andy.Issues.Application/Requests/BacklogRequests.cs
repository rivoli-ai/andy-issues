// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel.DataAnnotations;

namespace Andy.Issues.Application.Requests;

public record CreateEpicRequest(
    [Required] string Title,
    string? Description,
    int? Order,
    string? ExternalId);

public record UpdateEpicRequest(
    string? Title,
    string? Description,
    int? Order);

public record CreateFeatureRequest(
    [Required] string Title,
    string? Description,
    int? Order,
    string? ExternalId);

public record UpdateFeatureRequest(
    string? Title,
    string? Description,
    int? Order);

public record CreateUserStoryRequest(
    [Required] string Title,
    string? Description,
    string? AcceptanceCriteria,
    int? StoryPoints,
    int? Order,
    string? ExternalId);

public record UpdateUserStoryRequest(
    string? Title,
    string? Description,
    string? AcceptanceCriteria,
    int? StoryPoints,
    int? Order);

public record UpdateUserStoryStatusRequest(
    [Required] string Status,
    string? PullRequestUrl);
