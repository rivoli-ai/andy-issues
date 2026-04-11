// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Integration.Fakes;

public class FakeAzureDevOpsClient : IAzureDevOpsClient
{
    private readonly ConcurrentDictionary<string, AzureDevOpsRepositoryInfo> _responses = new();

    public void AddResponse(string organization, string project, string repositoryId, AzureDevOpsRepositoryInfo info)
    {
        _responses[Key(organization, project, repositoryId)] = info;
    }

    public void Reset() => _responses.Clear();

    public Task<AzureDevOpsRepositoryInfo?> GetRepositoryAsync(
        string organization,
        string project,
        string repositoryId,
        string personalAccessToken,
        CancellationToken ct = default)
    {
        _responses.TryGetValue(Key(organization, project, repositoryId), out var info);
        return Task.FromResult<AzureDevOpsRepositoryInfo?>(info);
    }

    private static string Key(string org, string project, string repoId) =>
        $"{org}|{project}|{repoId}";
}
