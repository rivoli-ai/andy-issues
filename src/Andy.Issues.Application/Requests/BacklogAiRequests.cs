// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.ComponentModel.DataAnnotations;

namespace Andy.Issues.Application.Requests;

/// <summary>
/// Request body for <c>POST /api/backlog/suggest</c>.
/// <para>
/// <see cref="Field"/> is one of <c>description</c> | <c>acceptanceCriteria</c>.
/// <see cref="ItemType"/> is one of <c>epic</c> | <c>feature</c> | <c>story</c>.
/// Only stories carry acceptance criteria in the domain schema, so
/// combinations like <c>(epic, acceptanceCriteria)</c> return 400.
/// </para>
/// <para>
/// <see cref="CurrentContent"/> is optional. When non-empty, the
/// service asks the LLM to refine the existing draft rather than
/// generate a fresh one.
/// </para>
/// <para>
/// <see cref="RepositoryId"/> is optional and scopes the prompt to a
/// repository the caller owns. When provided the service enforces the
/// same ownership check the rest of <c>/api/repositories/*</c> uses.
/// </para>
/// </summary>
public record SuggestContentRequest(
    [Required] string Field,
    [Required] string ItemType,
    [Required] string Title,
    string? CurrentContent,
    Guid? RepositoryId);
