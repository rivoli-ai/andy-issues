// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

public class RepositoriesControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RepositoriesControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<Guid> SeedRepoAsync(string owner, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner,
            Name = name,
            CloneUrl = $"https://example.com/{name}.git"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        return repo.Id;
    }

    [Fact]
    public async Task List_DefaultScopeMine_ReturnsOk()
    {
        await SeedRepoAsync("dev-user", "mine-repo");

        var response = await _client.GetAsync("/api/repositories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<PagedResult<RepositoryDto>>(JsonOptions);
        Assert.NotNull(body);
        Assert.Contains(body!.Items, r => r.Name == "mine-repo");
    }

    [Fact]
    public async Task List_InvalidScope_Returns400()
    {
        var response = await _client.GetAsync("/api/repositories?scope=bogus");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_ExistingRepoOwnedByCaller_ReturnsOk()
    {
        var id = await SeedRepoAsync("dev-user", "get-mine");
        var response = await _client.GetAsync($"/api/repositories/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_RepoOwnedByAnother_Returns404()
    {
        var id = await SeedRepoAsync("other-user", "not-mine");
        var response = await _client.GetAsync($"/api/repositories/{id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Owner_ReturnsNoContent()
    {
        var id = await SeedRepoAsync("dev-user", "to-delete");
        var response = await _client.DeleteAsync($"/api/repositories/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_NonOwner_Returns404()
    {
        var id = await SeedRepoAsync("other-user", "not-owned");
        var response = await _client.DeleteAsync($"/api/repositories/{id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
