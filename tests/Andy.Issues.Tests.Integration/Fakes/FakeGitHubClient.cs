// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Integration.Fakes;

public class FakeGitHubClient : IGitHubClient
{
    private readonly ConcurrentDictionary<string, GitHubRepositoryInfo> _responses = new();

    public void AddResponse(string fullName, GitHubRepositoryInfo info)
    {
        _responses[fullName] = info;
    }

    public void Reset() => _responses.Clear();

    public Task<GitHubRepositoryInfo?> GetRepositoryAsync(
        string fullName,
        string accessToken,
        CancellationToken ct = default)
    {
        _responses.TryGetValue(fullName, out var info);
        return Task.FromResult<GitHubRepositoryInfo?>(info);
    }
}
