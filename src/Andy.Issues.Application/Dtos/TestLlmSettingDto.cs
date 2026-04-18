// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

/// <summary>
/// Response body for <c>POST /api/llm-settings/{id}/test</c>. The
/// endpoint never 5xx's on a provider-side failure — it returns 200
/// with <c>Success=false</c> and the reason in <c>Message</c> so the
/// UI can render a red banner without dressing-up a network error.
/// Genuine 5xx means the request never reached the provider.
/// </summary>
public record TestLlmSettingDto(bool Success, string Message);
