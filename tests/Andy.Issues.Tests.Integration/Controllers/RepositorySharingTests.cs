// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

public class RepositorySharingTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RepositorySharingTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<Guid> SeedAsync(string repoName = "share-target")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "dev-user",
            Name = repoName,
            CloneUrl = $"https://example.com/{repoName}.git"
        };
        db.Repositories.Add(repo);

        if (!db.UserDirectory.Any(u => u.UserId == "dev-user"))
            db.UserDirectory.Add(new UserDirectoryEntry
            {
                Id = Guid.NewGuid(),
                UserId = "dev-user",
                Email = "dev@example.com"
            });
        if (!db.UserDirectory.Any(u => u.UserId == "target-user"))
            db.UserDirectory.Add(new UserDirectoryEntry
            {
                Id = Guid.NewGuid(),
                UserId = "target-user",
                Email = "target@example.com"
            });

        await db.SaveChangesAsync();
        return repo.Id;
    }

    [Fact]
    public async Task Share_Owner_Returns201AndBecomesVisibleToSharedUser()
    {
        var id = await SeedAsync();
        var req = new ShareRepositoryRequest("target@example.com");

        var response = await _client.PostAsJsonAsync($"/api/repositories/{id}/share", req);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var share = await response.Content.ReadFromJsonAsync<RepositoryShareDto>(JsonOptions);
        Assert.NotNull(share);
        Assert.Equal("target-user", share!.SharedWithUserId);
    }

    [Fact]
    public async Task Share_SelfEmail_Returns400()
    {
        var id = await SeedAsync("self-share");
        var req = new ShareRepositoryRequest("dev@example.com");

        var response = await _client.PostAsJsonAsync($"/api/repositories/{id}/share", req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Share_UnknownEmail_Returns404()
    {
        var id = await SeedAsync("unknown-email");
        var req = new ShareRepositoryRequest("ghost@example.com");

        var response = await _client.PostAsJsonAsync($"/api/repositories/{id}/share", req);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListShares_OwnerGetsShares()
    {
        var id = await SeedAsync("list-shares");
        await _client.PostAsJsonAsync($"/api/repositories/{id}/share",
            new ShareRepositoryRequest("target@example.com"));

        var response = await _client.GetAsync($"/api/repositories/{id}/shares");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var shares = await response.Content.ReadFromJsonAsync<List<RepositoryShareDto>>(JsonOptions);
        Assert.NotNull(shares);
        Assert.Contains(shares!, s => s.SharedWithUserId == "target-user");
    }

    [Fact]
    public async Task Unshare_ExistingShare_Returns204()
    {
        var id = await SeedAsync("unshare-me");
        await _client.PostAsJsonAsync($"/api/repositories/{id}/share",
            new ShareRepositoryRequest("target@example.com"));

        var response = await _client.DeleteAsync($"/api/repositories/{id}/share/target-user");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Unshare_NonExistent_Returns404()
    {
        var id = await SeedAsync("unshare-miss");
        var response = await _client.DeleteAsync($"/api/repositories/{id}/share/nobody");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
