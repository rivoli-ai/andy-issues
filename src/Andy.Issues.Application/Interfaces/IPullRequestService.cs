// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Requests;

namespace Andy.Issues.Application.Interfaces;

public enum PullRequestOutcome
{
    Created = 0,
    NotFound = 1,
    Forbidden = 2,
    PushFailed = 3,
    ProviderFailed = 4,
    UnsupportedProvider = 5
}

public record PullRequestResult(
    PullRequestOutcome Outcome,
    string? PullRequestUrl,
    string? Error);

public interface IPullRequestService
{
    Task<PullRequestResult> CreateFromSandboxAsync(
        Guid repositoryId,
        CreatePullRequestFromSandboxRequest request,
        string userId,
        CancellationToken ct = default);
}
