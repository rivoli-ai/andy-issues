// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

public class RepositoryGitHubSyncTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RepositoryGitHubSyncTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.FakeGitHubClient.Reset();
    }

    private async Task SeedPatAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.LinkedProviders.Any(p => p.OwnerUserId == "dev-user" && p.Provider == LinkedProviderKind.GitHub))
        {
            db.LinkedProviders.Add(new LinkedProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "dev-user",
                Provider = LinkedProviderKind.GitHub,
                AccessToken = "ghp_test"
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SyncGitHub_NoPat_Returns401()
    {
        // No SeedPatAsync call: the `dev-user` has no GitHub linked provider.
        // We must make sure no leftover row from other tests is present.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pat = db.LinkedProviders.FirstOrDefault(p =>
                p.OwnerUserId == "dev-user" && p.Provider == LinkedProviderKind.GitHub);
            if (pat is not null)
            {
                db.LinkedProviders.Remove(pat);
                await db.SaveChangesAsync();
            }
        }

        var req = new SyncGitHubRepositoriesRequest(new[] { "acme/r1" });
        var response = await _client.PostAsJsonAsync("/api/repositories/sync-github", req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SyncGitHub_WithPat_AddsRepository()
    {
        await SeedPatAsync();
        _factory.FakeGitHubClient.AddResponse(
            "acme/sync-1",
            new GitHubRepositoryInfo("999", "sync-1", "acme/sync-1", null, "https://github.com/acme/sync-1.git", "main"));

        var req = new SyncGitHubRepositoriesRequest(new[] { "acme/sync-1" });
        var response = await _client.PostAsJsonAsync("/api/repositories/sync-github", req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SyncResult>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(1, result!.Added);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Contains(db.Repositories, r => r.ExternalId == "999" && r.OwnerUserId == "dev-user");
    }
}
