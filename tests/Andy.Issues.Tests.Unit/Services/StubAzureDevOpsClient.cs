// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Unit.Services;

public class StubAzureDevOpsClient : IAzureDevOpsClient
{
    private readonly Dictionary<string, AzureDevOpsRepositoryInfo?> _responses = new();
    private readonly Dictionary<int, AzureDevOpsWorkItemSnapshot> _workItems = new();
    private int _nextId = 1000;

    public IReadOnlyDictionary<int, AzureDevOpsWorkItemSnapshot> WorkItems => _workItems;

    public List<AzureDevOpsWorkItemUpsert> UpsertCalls { get; } = new();

    public StubAzureDevOpsClient Returns(
        string organization,
        string project,
        string repositoryId,
        AzureDevOpsRepositoryInfo? info)
    {
        _responses[Key(organization, project, repositoryId)] = info;
        return this;
    }

    public StubAzureDevOpsClient SeedWorkItem(int id, string title, string state)
    {
        _workItems[id] = new AzureDevOpsWorkItemSnapshot(id, title, state);
        if (id >= _nextId) _nextId = id + 1;
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

    public Task<AzureDevOpsWorkItemSnapshot?> UpsertWorkItemAsync(
        string organization,
        string project,
        AzureDevOpsWorkItemUpsert item,
        string personalAccessToken,
        CancellationToken ct = default)
    {
        UpsertCalls.Add(item);
        int id = item.ExistingId ?? _nextId++;
        var snap = new AzureDevOpsWorkItemSnapshot(id, item.Title, item.State);
        _workItems[id] = snap;
        return Task.FromResult<AzureDevOpsWorkItemSnapshot?>(snap);
    }

    public Task<IReadOnlyList<AzureDevOpsWorkItemSnapshot>> GetWorkItemsAsync(
        string organization,
        string project,
        IReadOnlyList<int> ids,
        string personalAccessToken,
        CancellationToken ct = default)
    {
        var list = ids
            .Where(_workItems.ContainsKey)
            .Select(id => _workItems[id])
            .ToList();
        return Task.FromResult<IReadOnlyList<AzureDevOpsWorkItemSnapshot>>(list);
    }

    public List<(string org, string project, string repoId, string title, string? description, string source, string target)> PullRequestCalls { get; } = new();
    public AzureDevOpsPullRequestInfo? PullRequestResult { get; set; }

    public Task<AzureDevOpsPullRequestInfo?> CreatePullRequestAsync(
        string organization,
        string project,
        string repositoryId,
        string title,
        string? description,
        string sourceBranch,
        string targetBranch,
        string personalAccessToken,
        CancellationToken ct = default)
    {
        PullRequestCalls.Add((organization, project, repositoryId, title, description, sourceBranch, targetBranch));
        return Task.FromResult(PullRequestResult);
    }

    private static string Key(string org, string project, string repoId) =>
        $"{org}|{project}|{repoId}";
}
