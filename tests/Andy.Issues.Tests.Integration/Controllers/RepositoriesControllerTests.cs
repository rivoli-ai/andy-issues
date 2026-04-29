// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
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

    // ── #100 — provider filter ──────────────────────────────────────

    private async Task<Guid> SeedRepoWithProviderAsync(
        string owner, string name, Andy.Issues.Domain.Enums.RepositoryProvider provider)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new Andy.Issues.Domain.Entities.Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner,
            Name = name,
            CloneUrl = $"https://example.com/{name}.git",
            Provider = provider
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        return repo.Id;
    }

    [Fact]
    public async Task List_ProviderFilter_GitHub_ExcludesOtherProviders()
    {
        await SeedRepoWithProviderAsync("dev-user", "gh-repo", Andy.Issues.Domain.Enums.RepositoryProvider.GitHub);
        await SeedRepoWithProviderAsync("dev-user", "azdo-repo", Andy.Issues.Domain.Enums.RepositoryProvider.AzureDevOps);

        var response = await _client.GetAsync("/api/repositories?provider=github");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<PagedResult<RepositoryDto>>(JsonOptions);
        Assert.Contains(body!.Items, r => r.Name == "gh-repo");
        Assert.DoesNotContain(body.Items, r => r.Name == "azdo-repo");
    }

    [Fact]
    public async Task List_ProviderFilter_CaseInsensitive()
    {
        await SeedRepoWithProviderAsync("dev-user", "azdo-mixed", Andy.Issues.Domain.Enums.RepositoryProvider.AzureDevOps);

        var response = await _client.GetAsync("/api/repositories?provider=AZUREDEVOPS");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<RepositoryDto>>(JsonOptions);
        Assert.Contains(body!.Items, r => r.Name == "azdo-mixed");
    }

    [Fact]
    public async Task List_InvalidProvider_Returns400()
    {
        var response = await _client.GetAsync("/api/repositories?provider=bitbucket");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── #99 — ListAvailable ─────────────────────────────────────────

    // The factory is shared via IClassFixture; tests can leave
    // LinkedProvider/Repository rows behind. Strip dev-user state
    // upfront so each test starts from a known-empty baseline.
    private async Task ClearDevUserGitHubStateAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.LinkedProviders.RemoveRange(
            db.LinkedProviders.Where(p => p.OwnerUserId == "dev-user"
                && p.Provider == Andy.Issues.Domain.Enums.LinkedProviderKind.GitHub));
        db.Repositories.RemoveRange(
            db.Repositories.Where(r => r.OwnerUserId == "dev-user"
                && r.Provider == Andy.Issues.Domain.Enums.RepositoryProvider.GitHub
                && r.ExternalId != null));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ListAvailable_NoLinkedProvider_Returns404()
    {
        await ClearDevUserGitHubStateAsync();
        var resp = await _client.GetAsync("/api/repositories/available?provider=github");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task ListAvailable_AzureDevOps_Returns404_FollowUpScope()
    {
        var resp = await _client.GetAsync("/api/repositories/available?provider=azuredevops");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task ListAvailable_UnknownProvider_Returns400()
    {
        var resp = await _client.GetAsync("/api/repositories/available?provider=bitbucket");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ListAvailable_GitHub_LinkedProvider_FiltersOutAlreadySynced()
    {
        await ClearDevUserGitHubStateAsync();
        // Seed: LinkedProvider for dev-user, one already-synced GitHub
        // repo with ExternalId="42", and the FakeGitHubClient returns
        // both 42 and a fresh repo. The fresh one should land in
        // available.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LinkedProviders.Add(new Andy.Issues.Domain.Entities.LinkedProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "dev-user",
                Provider = Andy.Issues.Domain.Enums.LinkedProviderKind.GitHub,
                AccessToken = "ghp_token"
            });
            db.Repositories.Add(new Andy.Issues.Domain.Entities.Repository
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "dev-user",
                Name = "existing",
                CloneUrl = "https://github.com/acme/existing.git",
                ExternalId = "42",
                Provider = Andy.Issues.Domain.Enums.RepositoryProvider.GitHub
            });
            await db.SaveChangesAsync();
        }

        _factory.FakeGitHubClient.SetUserRepositories(new[]
        {
            new GitHubRepositoryInfo("42", "existing", "acme/existing", null, "https://github.com/acme/existing.git", "main"),
            new GitHubRepositoryInfo("43", "fresh", "acme/fresh", null, "https://github.com/acme/fresh.git", "main"),
        });

        var resp = await _client.GetAsync("/api/repositories/available?provider=github&page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<PagedResult<AvailableRepositoryDto>>(JsonOptions);

        Assert.Single(body!.Items);
        Assert.Equal("acme/fresh", body.Items[0].FullName);
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
