// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

/// <summary>
/// Response body for <c>POST /api/backlog/suggest</c>. Carries the
/// LLM-generated draft for the requested field. A single flat string
/// is intentional — the caller is going to drop it straight into a
/// TextEditor, so any ceremony (choices, candidate ranking, token
/// counts) belongs outside the response envelope.
/// </summary>
public record SuggestContentDto(string Suggestion);
