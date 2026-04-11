// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Unit.Services;

public class StubGitHubClient : IGitHubClient
{
    private readonly Dictionary<string, GitHubRepositoryInfo?> _responses = new();

    public StubGitHubClient Returns(string fullName, GitHubRepositoryInfo? info)
    {
        _responses[fullName] = info;
        return this;
    }

    public Task<GitHubRepositoryInfo?> GetRepositoryAsync(
        string fullName,
        string accessToken,
        CancellationToken ct = default)
    {
        _responses.TryGetValue(fullName, out var info);
        return Task.FromResult(info);
    }
}
