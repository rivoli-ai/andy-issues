// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Domain.Enums;

/// <summary>
/// Result states for <c>ILlmSettingService.TestAsync</c>. The
/// distinction between <see cref="ProviderRejected"/> and the
/// general 500 controller fallback matters: rejection is a live
/// "your key is bad" signal (user actionable, rendered in a red
/// banner), while 500 is a bug on our side.
/// </summary>
public enum TestLlmSettingOutcome
{
    Ok = 0,
    NotFound = 1,
    ProviderRejected = 2
}
