// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.External;

public class GitHubClient : IGitHubClient
{
    private const string BaseUrl = "https://api.github.com";
    private const string UserAgent = "andy-issues";
    private const string ApiVersion = "2022-11-28";

    private readonly HttpClient _http;
    private readonly ILogger<GitHubClient> _logger;

    public GitHubClient(HttpClient http, ILogger<GitHubClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<GitHubRepositoryInfo?> GetRepositoryAsync(
        string fullName,
        string accessToken,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/repos/{fullName}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("GitHub GET /repos/{FullName} failed with {Status}",
                fullName, response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        return new GitHubRepositoryInfo(
            ExternalId: root.GetProperty("id").GetRawText(),
            Name: root.GetProperty("name").GetString() ?? fullName,
            FullName: root.GetProperty("full_name").GetString() ?? fullName,
            Description: root.TryGetProperty("description", out var desc) && desc.ValueKind != JsonValueKind.Null
                ? desc.GetString() : null,
            CloneUrl: root.GetProperty("clone_url").GetString() ?? "",
            DefaultBranch: root.GetProperty("default_branch").GetString() ?? "main");
    }
}
