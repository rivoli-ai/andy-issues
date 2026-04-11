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
}
