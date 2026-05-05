// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.PullRequests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

// #90 — GET /api/pr-head-branch?url=…
public class PullRequestHeadBranchTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PullRequestHeadBranchTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task SetLinkedProviderAsync(string userId, LinkedProviderKind kind, string token)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.LinkedProviders
            .FirstOrDefaultAsync(p => p.OwnerUserId == userId && p.Provider == kind);
        if (existing is not null) db.LinkedProviders.Remove(existing);
        db.LinkedProviders.Add(new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userId,
            Provider = kind,
            AccessToken = token
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GitHub_PrUrl_Returns200WithBranch()
    {
        await SetLinkedProviderAsync("dev-user", LinkedProviderKind.GitHub, "ghp_tok");
        _factory.FakeGitHubClient.SetPullRequestStatus(
            "acme", "widget", 42,
            new PullRequestStatusInfo("open", false, null, "feature/login"));

        var url = "https://github.com/acme/widget/pull/42";
        var resp = await _client.GetAsync($"/api/pr-head-branch?url={Uri.EscapeDataString(url)}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<PullRequestHeadBranchDto>(JsonOptions);
        Assert.Equal("feature/login", dto!.Branch);
    }

    [Fact]
    public async Task AzureDevOps_PrUrl_Returns200WithBranch()
    {
        await SetLinkedProviderAsync("dev-user", LinkedProviderKind.AzureDevOps, "ado_tok");
        _factory.FakeAzureDevOpsClient.SetPullRequestStatus(
            "myorg", "myproject", "myrepo", 17,
            new PullRequestStatusInfo("open", false, null, "feature/azure"));

        var url = "https://dev.azure.com/myorg/myproject/_git/myrepo/pullrequest/17";
        var resp = await _client.GetAsync($"/api/pr-head-branch?url={Uri.EscapeDataString(url)}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<PullRequestHeadBranchDto>(JsonOptions);
        Assert.Equal("feature/azure", dto!.Branch);
    }

    [Fact]
    public async Task UnrecognisedUrl_Returns400()
    {
        var resp = await _client.GetAsync(
            "/api/pr-head-branch?url=" + Uri.EscapeDataString("https://gitlab.com/x/y/-/merge_requests/1"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task NoLinkedProvider_Returns404()
    {
        // Strip any GitHub provider for dev-user from prior tests on
        // the shared factory.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LinkedProviders.RemoveRange(
                db.LinkedProviders.Where(p =>
                    p.OwnerUserId == "dev-user" && p.Provider == LinkedProviderKind.GitHub));
            await db.SaveChangesAsync();
        }

        var url = "https://github.com/acme/widget/pull/99";
        var resp = await _client.GetAsync($"/api/pr-head-branch?url={Uri.EscapeDataString(url)}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task PrNotFound_Returns404()
    {
        await SetLinkedProviderAsync("dev-user", LinkedProviderKind.GitHub, "ghp_tok");
        // No SetPullRequestStatus → fake returns null.
        var url = "https://github.com/acme/widget/pull/9999";
        var resp = await _client.GetAsync($"/api/pr-head-branch?url={Uri.EscapeDataString(url)}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task MissingUrlParam_Returns400()
    {
        var resp = await _client.GetAsync("/api/pr-head-branch");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
