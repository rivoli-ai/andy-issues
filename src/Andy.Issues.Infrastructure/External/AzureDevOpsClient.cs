// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.External;

public class AzureDevOpsClient : IAzureDevOpsClient
{
    private const string ApiVersion = "7.1";

    private readonly HttpClient _http;
    private readonly ILogger<AzureDevOpsClient> _logger;

    public AzureDevOpsClient(HttpClient http, ILogger<AzureDevOpsClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<AzureDevOpsRepositoryInfo?> GetRepositoryAsync(
        string organization,
        string project,
        string repositoryId,
        string personalAccessToken,
        CancellationToken ct = default)
    {
        var url = $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}?api-version={ApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{personalAccessToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Azure DevOps GET {Organization}/{Project}/repositories/{RepositoryId} failed: {Status}",
                organization, project, repositoryId, response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var defaultBranch = "main";
        if (root.TryGetProperty("defaultBranch", out var db) && db.ValueKind == JsonValueKind.String)
        {
            var value = db.GetString() ?? "";
            const string prefix = "refs/heads/";
            defaultBranch = value.StartsWith(prefix, StringComparison.Ordinal)
                ? value[prefix.Length..]
                : value;
        }

        return new AzureDevOpsRepositoryInfo(
            ExternalId: root.GetProperty("id").GetString() ?? repositoryId,
            Name: root.GetProperty("name").GetString() ?? repositoryId,
            Description: root.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String
                ? desc.GetString() : null,
            CloneUrl: root.TryGetProperty("remoteUrl", out var remote) ? remote.GetString() ?? "" : "",
            DefaultBranch: defaultBranch,
            Project: project,
            Organization: organization);
    }

    public async Task<AzureDevOpsWorkItemSnapshot?> UpsertWorkItemAsync(
        string organization,
        string project,
        AzureDevOpsWorkItemUpsert item,
        string personalAccessToken,
        CancellationToken ct = default)
    {
        var patch = new List<object>
        {
            new { op = "add", path = "/fields/System.Title", value = item.Title },
            new { op = "add", path = "/fields/System.State", value = item.State }
        };
        if (!string.IsNullOrWhiteSpace(item.Description))
            patch.Add(new { op = "add", path = "/fields/System.Description", value = item.Description });

        var url = item.ExistingId is int existing
            ? $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/{existing}?api-version={ApiVersion}"
            : $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/$User%20Story?api-version={ApiVersion}";

        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{personalAccessToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var json = JsonSerializer.Serialize(patch);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Azure DevOps UpsertWorkItem {Organization}/{Project} id={ExistingId} failed: {Status}",
                organization, project, item.ExistingId, response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ReadSnapshot(doc.RootElement);
    }

    public async Task<IReadOnlyList<AzureDevOpsWorkItemSnapshot>> GetWorkItemsAsync(
        string organization,
        string project,
        IReadOnlyList<int> ids,
        string personalAccessToken,
        CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return Array.Empty<AzureDevOpsWorkItemSnapshot>();

        var idList = string.Join(",", ids);
        var url = $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/wit/workitems?ids={idList}&fields=System.Id,System.Title,System.State&api-version={ApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{personalAccessToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Azure DevOps GetWorkItems {Organization}/{Project} failed: {Status}",
                organization, project, response.StatusCode);
            return Array.Empty<AzureDevOpsWorkItemSnapshot>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        if (!root.TryGetProperty("value", out var items) || items.ValueKind != JsonValueKind.Array)
            return Array.Empty<AzureDevOpsWorkItemSnapshot>();

        var results = new List<AzureDevOpsWorkItemSnapshot>(items.GetArrayLength());
        foreach (var el in items.EnumerateArray())
        {
            var snap = ReadSnapshot(el);
            if (snap is not null) results.Add(snap);
        }
        return results;
    }

    private static AzureDevOpsWorkItemSnapshot? ReadSnapshot(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idEl))
            return null;
        int id = idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32() : int.Parse(idEl.GetString() ?? "0");
        string title = "";
        string state = "";
        if (root.TryGetProperty("fields", out var fields))
        {
            if (fields.TryGetProperty("System.Title", out var t) && t.ValueKind == JsonValueKind.String)
                title = t.GetString() ?? "";
            if (fields.TryGetProperty("System.State", out var s) && s.ValueKind == JsonValueKind.String)
                state = s.GetString() ?? "";
        }
        return new AzureDevOpsWorkItemSnapshot(id, title, state);
    }
}
