// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Andy.Issues.Application.Dtos;

public enum UserStoryStatusUpdateOutcome
{
    Updated = 0,
    NotFound = 1,
    InvalidStatus = 2,
    InvalidTransition = 3
}

public record UserStoryStatusUpdateResult(
    UserStoryStatusUpdateOutcome Outcome,
    UserStoryDto? Story,
    string? Error)
{
    public static UserStoryStatusUpdateResult Ok(UserStoryDto story) =>
        new(UserStoryStatusUpdateOutcome.Updated, story, null);

    public static UserStoryStatusUpdateResult NotFound() =>
        new(UserStoryStatusUpdateOutcome.NotFound, null, null);

    public static UserStoryStatusUpdateResult InvalidStatus(string value) =>
        new(UserStoryStatusUpdateOutcome.InvalidStatus, null, $"Unknown status '{value}'.");

    public static UserStoryStatusUpdateResult InvalidTransition(string message) =>
        new(UserStoryStatusUpdateOutcome.InvalidTransition, null, message);
}
