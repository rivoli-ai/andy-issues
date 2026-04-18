// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Enums;

/// <summary>
/// Result states for <c>IBacklogAiService.SuggestContentAsync</c>. Each
/// value maps 1:1 to an HTTP status in <c>BacklogController.SuggestContent</c>
/// so the controller layer stays free of business logic.
/// </summary>
public enum SuggestContentOutcome
{
    Ok = 0,
    NoLlmSetting = 1,
    RepositoryNotFound = 2,
    NotOwner = 3,
    InvalidField = 4,
    InvalidItemType = 5,
    LlmCallFailed = 6,
    ParseFailed = 7
}
