// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using Andy.Issues.Application.Interfaces;

namespace Andy.Issues.Tests.Integration.Fakes;

public class FakeAzureDevOpsClient : IAzureDevOpsClient
{
    private readonly ConcurrentDictionary<string, AzureDevOpsRepositoryInfo> _responses = new();
    private readonly ConcurrentDictionary<int, AzureDevOpsWorkItemSnapshot> _workItems = new();
    private int _nextId = 1000;

    public IReadOnlyDictionary<int, AzureDevOpsWorkItemSnapshot> WorkItems => _workItems;

    public void AddResponse(string organization, string project, string repositoryId, AzureDevOpsRepositoryInfo info)
    {
        _responses[Key(organization, project, repositoryId)] = info;
    }

    public void SetWorkItemState(int id, string title, string state)
    {
        _workItems[id] = new AzureDevOpsWorkItemSnapshot(id, title, state);
        if (id >= _nextId) _nextId = id + 1;
    }

    public void Reset()
    {
        _responses.Clear();
        _workItems.Clear();
        _nextId = 1000;
        _prCalls.Clear();
        PullRequestResult = null;
    }

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

    public Task<AzureDevOpsWorkItemSnapshot?> UpsertWorkItemAsync(
        string organization,
        string project,
        AzureDevOpsWorkItemUpsert item,
        string personalAccessToken,
        CancellationToken ct = default)
    {
        int id = item.ExistingId ?? Interlocked.Increment(ref _nextId);
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

    private readonly ConcurrentBag<(string org, string project, string repoId, string title, string? description, string source, string target)> _prCalls = new();

    public AzureDevOpsPullRequestInfo? PullRequestResult { get; set; }
    public IReadOnlyCollection<(string org, string project, string repoId, string title, string? description, string source, string target)> PullRequestCalls => _prCalls;

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
        _prCalls.Add((organization, project, repositoryId, title, description, sourceBranch, targetBranch));
        return Task.FromResult(PullRequestResult);
    }

    public ConcurrentDictionary<string, IReadOnlyList<AzureDevOpsFeedInfo>> FeedResponses { get; } = new();

    public Task<IReadOnlyList<AzureDevOpsFeedInfo>> ListFeedsAsync(
        string organization,
        string personalAccessToken,
        CancellationToken ct = default)
    {
        FeedResponses.TryGetValue(organization, out var feeds);
        return Task.FromResult<IReadOnlyList<AzureDevOpsFeedInfo>>(
            feeds ?? Array.Empty<AzureDevOpsFeedInfo>());
    }

    private static string Key(string org, string project, string repoId) =>
        $"{org}|{project}|{repoId}";
}
