// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

public class RepositoryAzurePatIdentityTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RepositoryAzurePatIdentityTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.FakeAzureDevOpsClient.ConnectionResults.Clear();
        _factory.FakeAzureDevOpsClient.DefaultConnectionResult =
            new AzureDevOpsConnectionInfo("fake-user-id", "fake-azdo-user");
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<Guid> SeedRepoAsync(string owner = "dev-user")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner,
            Name = $"pat-test-{Guid.NewGuid():N}",
            CloneUrl = $"https://example.com/{Guid.NewGuid():N}.git"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        return repo.Id;
    }

    [Fact]
    public async Task PatchAzurePat_Owner_Returns204AndPersistsColumns()
    {
        var id = await SeedRepoAsync();

        var response = await _client.PatchAsJsonAsync(
            $"/api/repositories/{id}/azure-pat-identity",
            new UpdateRepositoryAzurePatIdentityRequest("contoso", "my-project", "the-pat"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = await db.Repositories.AsNoTracking().FirstAsync(r => r.Id == id);
        Assert.Equal("contoso", repo.AzureOrganization);
        Assert.Equal("my-project", repo.AzureProject);
        Assert.False(string.IsNullOrEmpty(repo.AzurePat));
    }

    [Fact]
    public async Task PatchAzurePat_OtherUsersRepo_Returns404()
    {
        var id = await SeedRepoAsync(owner: "other-user");

        var response = await _client.PatchAsJsonAsync(
            $"/api/repositories/{id}/azure-pat-identity",
            new UpdateRepositoryAzurePatIdentityRequest("contoso", "proj", "pat"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PatchAzurePat_ClearsServicePrincipalFields()
    {
        var id = await SeedRepoAsync();

        var spResponse = await _client.PatchAsJsonAsync(
            $"/api/repositories/{id}/azure-identity",
            new UpdateRepositoryAzureIdentityRequest("cid", "secret", "tid", "sub"));
        Assert.Equal(HttpStatusCode.NoContent, spResponse.StatusCode);

        var patResponse = await _client.PatchAsJsonAsync(
            $"/api/repositories/{id}/azure-pat-identity",
            new UpdateRepositoryAzurePatIdentityRequest("contoso", "proj", "pat"));
        Assert.Equal(HttpStatusCode.NoContent, patResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = await db.Repositories.AsNoTracking().FirstAsync(r => r.Id == id);
        Assert.Null(repo.AzureClientId);
        Assert.Null(repo.AzureClientSecret);
        Assert.Null(repo.AzureTenantId);
        Assert.Null(repo.AzureSubscriptionId);
    }

    [Fact]
    public async Task Verify_PatIdentity_CallsConnectionData_ReturnsSuccess()
    {
        var id = await SeedRepoAsync();

        await _client.PatchAsJsonAsync(
            $"/api/repositories/{id}/azure-pat-identity",
            new UpdateRepositoryAzurePatIdentityRequest("contoso", "proj", "the-pat"));

        _factory.FakeAzureDevOpsClient.ConnectionResults["contoso"] =
            new AzureDevOpsConnectionInfo("authenticated-user-id", "Alice");

        var response = await _client.PostAsync($"/api/repositories/{id}/verify-azure-identity", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Contains("Alice", body.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task Verify_PatIdentity_InvalidPat_ReturnsFailure()
    {
        var id = await SeedRepoAsync();

        await _client.PatchAsJsonAsync(
            $"/api/repositories/{id}/azure-pat-identity",
            new UpdateRepositoryAzurePatIdentityRequest("badorg", "proj", "bad-pat"));

        _factory.FakeAzureDevOpsClient.ConnectionResults["badorg"] = null;

        var response = await _client.PostAsync($"/api/repositories/{id}/verify-azure-identity", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Contains("badorg", body.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task Verify_ServicePrincipalIdentity_DoesNotCallConnectionData()
    {
        var id = await SeedRepoAsync();

        await _client.PatchAsJsonAsync(
            $"/api/repositories/{id}/azure-identity",
            new UpdateRepositoryAzureIdentityRequest("cid", "secret", "tid", null));

        // Seed an explicit failure for what would be the "wrong branch" —
        // if the verify path erroneously hits the PAT code, the response
        // would carry that failure message.
        _factory.FakeAzureDevOpsClient.DefaultConnectionResult = null;

        var response = await _client.PostAsync($"/api/repositories/{id}/verify-azure-identity", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Contains("service-principal", body.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Verify_NoIdentityConfigured_ReturnsNotConfigured()
    {
        var id = await SeedRepoAsync();
        var response = await _client.PostAsync($"/api/repositories/{id}/verify-azure-identity", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.False(body.GetProperty("success").GetBoolean());
    }
}
