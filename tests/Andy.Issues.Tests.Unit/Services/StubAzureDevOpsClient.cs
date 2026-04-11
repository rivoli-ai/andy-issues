// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Unit.Services;

public class StubAzureDevOpsClient : IAzureDevOpsClient
{
    private readonly Dictionary<string, AzureDevOpsRepositoryInfo?> _responses = new();

    public StubAzureDevOpsClient Returns(
        string organization,
        string project,
        string repositoryId,
        AzureDevOpsRepositoryInfo? info)
    {
        _responses[Key(organization, project, repositoryId)] = info;
        return this;
    }

    public Task<AzureDevOpsRepositoryInfo?> GetRepositoryAsync(
        string organization,
        string project,
        string repositoryId,
        string personalAccessToken,
        CancellationToken ct = default)
    {
        _responses.TryGetValue(Key(organization, project, repositoryId), out var info);
        return Task.FromResult(info);
    }

    private static string Key(string org, string project, string repoId) =>
        $"{org}|{project}|{repoId}";
}
