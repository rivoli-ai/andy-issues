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

public class RepositoryAzureDevOpsSyncTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RepositoryAzureDevOpsSyncTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.FakeAzureDevOpsClient.Reset();
    }

    private async Task EnsurePatAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.LinkedProviders.Any(p => p.OwnerUserId == "dev-user" && p.Provider == LinkedProviderKind.AzureDevOps))
        {
            db.LinkedProviders.Add(new LinkedProvider
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "dev-user",
                Provider = LinkedProviderKind.AzureDevOps,
                AccessToken = "pat-value"
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SyncAzure_NoPat_Returns401()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pat = db.LinkedProviders.FirstOrDefault(p =>
                p.OwnerUserId == "dev-user" && p.Provider == LinkedProviderKind.AzureDevOps);
            if (pat is not null)
            {
                db.LinkedProviders.Remove(pat);
                await db.SaveChangesAsync();
            }
        }

        var req = new SyncAzureDevOpsRepositoriesRequest("contoso", "proj", new[] { "id1" });
        var response = await _client.PostAsJsonAsync("/api/repositories/sync-azure", req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SyncAzure_WithPat_AddsRepository()
    {
        await EnsurePatAsync();
        _factory.FakeAzureDevOpsClient.AddResponse(
            "contoso", "proj", "azdo-repo",
            new AzureDevOpsRepositoryInfo(
                "guid-azdo", "azdo-repo", "desc",
                "https://dev.azure.com/contoso/proj/_git/azdo-repo",
                "main", "proj", "contoso"));

        var req = new SyncAzureDevOpsRepositoriesRequest("contoso", "proj", new[] { "azdo-repo" });
        var response = await _client.PostAsJsonAsync("/api/repositories/sync-azure", req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SyncResult>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(1, result!.Added);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Contains(db.Repositories, r =>
            r.ExternalId == "guid-azdo" &&
            r.Provider == RepositoryProvider.AzureDevOps &&
            r.OwnerUserId == "dev-user");
    }
}
