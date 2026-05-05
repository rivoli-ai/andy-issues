// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

// #91 + #92 — agent-rules read/write per repository.
public class RepositoryAgentRulesTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RepositoryAgentRulesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<Guid> SeedRepoAsync(string ownerUserId, string? agentRules = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Name = "rules-repo",
            CloneUrl = $"https://example.com/{Guid.NewGuid():N}.git",
            AgentRules = agentRules
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        return repo.Id;
    }

    private async Task ShareWithAsync(Guid repositoryId, string ownerUserId, string sharedWithUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RepositoryShares.Add(new RepositoryShare
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            SharedWithUserId = sharedWithUserId,
            GrantedByUserId = ownerUserId,
            GrantedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_NoRulesSet_Returns200WithEmptyString()
    {
        var id = await SeedRepoAsync("dev-user");

        var resp = await _client.GetAsync($"/api/repositories/{id}/agent-rules");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AgentRulesDto>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(string.Empty, body!.Rules);
    }

    [Fact]
    public async Task Get_RulesSet_ReturnsRules()
    {
        var id = await SeedRepoAsync("dev-user", agentRules: "always run dotnet format");

        var resp = await _client.GetAsync($"/api/repositories/{id}/agent-rules");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AgentRulesDto>(JsonOptions);
        Assert.Equal("always run dotnet format", body!.Rules);
    }

    [Fact]
    public async Task Get_NotOwnedAndNotShared_Returns404()
    {
        var id = await SeedRepoAsync("other-user", agentRules: "secret rules");

        var resp = await _client.GetAsync($"/api/repositories/{id}/agent-rules");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Get_SharedWithCaller_Returns200()
    {
        var id = await SeedRepoAsync("other-user", agentRules: "shared rules");
        await ShareWithAsync(id, "other-user", "dev-user");

        var resp = await _client.GetAsync($"/api/repositories/{id}/agent-rules");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AgentRulesDto>(JsonOptions);
        Assert.Equal("shared rules", body!.Rules);
    }

    [Fact]
    public async Task Put_Owner_PersistsAndReturns204()
    {
        var id = await SeedRepoAsync("dev-user");

        var resp = await _client.PutAsJsonAsync(
            $"/api/repositories/{id}/agent-rules",
            new { rules = "use 4-space indent" });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Repositories
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => r.AgentRules)
            .FirstAsync();
        Assert.Equal("use 4-space indent", saved);
    }

    [Fact]
    public async Task Put_EmptyString_ClearsRules()
    {
        var id = await SeedRepoAsync("dev-user", agentRules: "old rules");

        var resp = await _client.PutAsJsonAsync(
            $"/api/repositories/{id}/agent-rules",
            new { rules = "" });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Repositories
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => r.AgentRules)
            .FirstAsync();
        Assert.Null(saved);
    }

    [Fact]
    public async Task Put_NotOwner_Returns404()
    {
        var id = await SeedRepoAsync("other-user");

        var resp = await _client.PutAsJsonAsync(
            $"/api/repositories/{id}/agent-rules",
            new { rules = "trying to overwrite" });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Put_SharedNotOwner_Returns404()
    {
        var id = await SeedRepoAsync("other-user");
        await ShareWithAsync(id, "other-user", "dev-user");

        var resp = await _client.PutAsJsonAsync(
            $"/api/repositories/{id}/agent-rules",
            new { rules = "share-write attempt" });
        // Owner-only on PUT — sharers can read but not write.
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Put_Oversize_Returns400()
    {
        var id = await SeedRepoAsync("dev-user");

        // 65537 chars — one byte over the column cap.
        var oversized = new string('x', 65537);
        var resp = await _client.PutAsJsonAsync(
            $"/api/repositories/{id}/agent-rules",
            new { rules = oversized });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Put_WritesAuditLog()
    {
        var id = await SeedRepoAsync("dev-user");

        await _client.PutAsJsonAsync(
            $"/api/repositories/{id}/agent-rules",
            new { rules = "audited content" });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.AuditLog
            .AsNoTracking()
            .FirstOrDefaultAsync(a =>
                a.ResourceType == "Repository"
                && a.ResourceId == id.ToString()
                && a.Action == "RepositoryAgentRulesUpdated");
        Assert.NotNull(entry);
        Assert.Equal("dev-user", entry!.UserId);
        // Length is captured in details so the audit row stays small
        // even when the rule blob itself is large.
        Assert.Equal("length=15", entry.Details);
    }
}
