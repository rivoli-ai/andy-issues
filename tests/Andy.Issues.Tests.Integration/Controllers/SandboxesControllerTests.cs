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

public class SandboxesControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SandboxesControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.FakeContainersClient.Reset();
    }

    private async Task<Guid> SeedRepoAsync(string owner = "dev-user")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner,
            Name = $"sbx-{Guid.NewGuid():N}",
            CloneUrl = "https://example.com/s.git"
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        return repo.Id;
    }

    [Fact]
    public async Task FullCrud_HappyPath()
    {
        var repoId = await SeedRepoAsync();

        // Create
        var createResp = await _client.PostAsJsonAsync("/api/sandboxes",
            new CreateSandboxRequest(repoId, "feature/test", null));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<SandboxDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal("feature/test", created!.Branch);
        Assert.Single(_factory.FakeContainersClient.CreateCalls);

        // Flip the fake container to Running so list/get reflect live status.
        _factory.FakeContainersClient.SetStatus(created.ContainerId, "Running");

        // List
        var listResp = await _client.GetAsync("/api/sandboxes");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var list = await listResp.Content.ReadFromJsonAsync<List<SandboxDto>>(JsonOptions);
        Assert.Contains(list!, s => s.Id == created.Id && s.Status == "Running");

        // Get by id
        var getResp = await _client.GetAsync($"/api/sandboxes/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var fetched = await getResp.Content.ReadFromJsonAsync<SandboxDto>(JsonOptions);
        Assert.Equal("Running", fetched!.Status);

        // Connection info
        var connResp = await _client.GetAsync($"/api/sandboxes/{created.Id}/connection");
        Assert.Equal(HttpStatusCode.OK, connResp.StatusCode);
        var conn = await connResp.Content.ReadFromJsonAsync<SandboxConnectionDto>(JsonOptions);
        Assert.Equal("https://ide/1", conn!.IdeEndpoint);

        // Delete
        var delResp = await _client.DeleteAsync($"/api/sandboxes/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);
        Assert.Contains(created.ContainerId, _factory.FakeContainersClient.DestroyCalls);

        var afterResp = await _client.GetAsync($"/api/sandboxes/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterResp.StatusCode);
    }

    [Fact]
    public async Task Create_StrangerRepo_Returns404()
    {
        var repoId = await SeedRepoAsync(owner: "someone-else");
        var resp = await _client.PostAsJsonAsync("/api/sandboxes",
            new CreateSandboxRequest(repoId, "main", null));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Empty(_factory.FakeContainersClient.CreateCalls);
    }

    [Fact]
    public async Task Get_OtherUsersSandbox_Returns404()
    {
        // Seed a sandbox owned by someone else directly so we can target it.
        var repoId = await SeedRepoAsync();
        string containerId;
        Guid sandboxId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sb = new Sandbox
            {
                Id = Guid.NewGuid(),
                ContainerId = $"ctr-other-{Guid.NewGuid():N}",
                RepositoryId = repoId,
                OwnerUserId = "other-user",
                Branch = "main"
            };
            db.Sandboxes.Add(sb);
            await db.SaveChangesAsync();
            sandboxId = sb.Id;
            containerId = sb.ContainerId;
        }

        var getResp = await _client.GetAsync($"/api/sandboxes/{sandboxId}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);

        var delResp = await _client.DeleteAsync($"/api/sandboxes/{sandboxId}");
        Assert.Equal(HttpStatusCode.NotFound, delResp.StatusCode);

        Assert.DoesNotContain(containerId, _factory.FakeContainersClient.DestroyCalls);
    }
}
