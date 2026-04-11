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

public class McpControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public McpControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.ResetPermissions();
    }

    [Fact]
    public async Task Post_PersonalStdio_ReturnsCreatedWithMaskedSecrets()
    {
        var req = new CreateMcpServerConfigRequest(
            Name: $"live-{Guid.NewGuid():N}",
            Description: "test",
            Type: "Stdio",
            Command: "python",
            ArgumentsJson: "[\"-m\",\"mcp\"]",
            EnvironmentJson: "{\"API_KEY\":\"integration-secret\"}",
            Url: null,
            HeadersJson: null,
            IsShared: false);

        var resp = await _client.PostAsJsonAsync("/api/mcp", req);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("integration-secret", body);

        var dto = await resp.Content.ReadFromJsonAsync<McpServerConfigDto>(JsonOptions);
        Assert.True(dto!.HasEnvironment);
        Assert.False(dto.HasHeaders);
    }

    [Fact]
    public async Task Post_Shared_NonAdmin_Returns403()
    {
        _factory.PermissionCheckerFake.SetAdmin(false);
        var req = new CreateMcpServerConfigRequest(
            Name: $"shared-{Guid.NewGuid():N}",
            Description: null,
            Type: "Remote",
            Command: null,
            ArgumentsJson: null,
            EnvironmentJson: null,
            Url: "https://mcp.example.com",
            HeadersJson: null,
            IsShared: true);

        var resp = await _client.PostAsJsonAsync("/api/mcp", req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Post_Shared_Admin_Succeeds()
    {
        _factory.PermissionCheckerFake.SetAdmin(true);
        var req = new CreateMcpServerConfigRequest(
            Name: $"admin-shared-{Guid.NewGuid():N}",
            Description: null,
            Type: "Remote",
            Command: null,
            ArgumentsJson: null,
            EnvironmentJson: null,
            Url: "https://mcp.example.com",
            HeadersJson: "{\"Authorization\":\"Bearer hush\"}",
            IsShared: true);

        var resp = await _client.PostAsJsonAsync("/api/mcp", req);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("hush", body);
    }

    [Fact]
    public async Task Toggle_Owner_FlipsEnabled()
    {
        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = new McpServerConfig
            {
                Id = Guid.NewGuid(),
                Name = $"toggle-{Guid.NewGuid():N}",
                OwnerUserId = "dev-user",
                Type = McpServerType.Stdio,
                Command = "py",
                Enabled = true
            };
            db.McpServerConfigs.Add(entity);
            await db.SaveChangesAsync();
            id = entity.Id;
        }

        var resp = await _client.PostAsync($"/api/mcp/{id}/toggle", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<McpServerConfigDto>(JsonOptions);
        Assert.False(dto!.Enabled);
    }

    [Fact]
    public async Task List_ContainsPersonalAndShared_SecretsMasked()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.McpServerConfigs.Add(new McpServerConfig
            {
                Id = Guid.NewGuid(),
                Name = $"list-shared-{Guid.NewGuid():N}",
                IsShared = true,
                Type = McpServerType.Stdio,
                Command = "py",
                EnvironmentJson = "{\"S\":\"very-secret\"}"
            });
            await db.SaveChangesAsync();
        }

        var resp = await _client.GetAsync("/api/mcp");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("very-secret", body);
    }

    [Fact]
    public async Task DiscoverTools_Remote_HappyPath_Returns200()
    {
        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = new McpServerConfig
            {
                Id = Guid.NewGuid(),
                Name = $"remote-{Guid.NewGuid():N}",
                OwnerUserId = "dev-user",
                Type = McpServerType.Remote,
                Url = "https://mcp.example.com"
            };
            db.McpServerConfigs.Add(entity);
            await db.SaveChangesAsync();
            id = entity.Id;
        }

        _factory.FakeMcpToolDiscoveryClient.Reset();
        _factory.FakeMcpToolDiscoveryClient.Result = new McpToolDiscoveryResult(
            McpToolDiscoveryOutcome.Ok,
            new[] { new McpToolDescriptor("search", "search the repo", null) },
            null);

        var resp = await _client.PostAsync($"/api/mcp/{id}/tools", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var tools = doc.RootElement.GetProperty("tools").EnumerateArray().ToList();
        Assert.Single(tools);
        Assert.Equal("search", tools[0].GetProperty("name").GetString());
        Assert.Single(_factory.FakeMcpToolDiscoveryClient.Calls);
    }

    [Fact]
    public async Task DiscoverTools_Stdio_Returns400()
    {
        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = new McpServerConfig
            {
                Id = Guid.NewGuid(),
                Name = $"stdio-{Guid.NewGuid():N}",
                OwnerUserId = "dev-user",
                Type = McpServerType.Stdio,
                Command = "py"
            };
            db.McpServerConfigs.Add(entity);
            await db.SaveChangesAsync();
            id = entity.Id;
        }

        var resp = await _client.PostAsync($"/api/mcp/{id}/tools", null);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task DiscoverTools_DiscoveryFailure_Returns502WithOutcome()
    {
        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = new McpServerConfig
            {
                Id = Guid.NewGuid(),
                Name = $"fail-{Guid.NewGuid():N}",
                OwnerUserId = "dev-user",
                Type = McpServerType.Remote,
                Url = "https://mcp.example.com"
            };
            db.McpServerConfigs.Add(entity);
            await db.SaveChangesAsync();
            id = entity.Id;
        }

        _factory.FakeMcpToolDiscoveryClient.Result = new McpToolDiscoveryResult(
            McpToolDiscoveryOutcome.Timeout, null, "Discovery timed out.");

        var resp = await _client.PostAsync($"/api/mcp/{id}/tools", null);
        Assert.Equal(HttpStatusCode.BadGateway, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("Timeout", doc.RootElement.GetProperty("discoveryOutcome").GetString());
    }

    [Fact]
    public async Task Delete_Admin_SharedConfig_Returns204()
    {
        _factory.PermissionCheckerFake.SetAdmin(true);
        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = new McpServerConfig
            {
                Id = Guid.NewGuid(),
                Name = $"admin-del-{Guid.NewGuid():N}",
                IsShared = true,
                Type = McpServerType.Stdio,
                Command = "py"
            };
            db.McpServerConfigs.Add(entity);
            await db.SaveChangesAsync();
            id = entity.Id;
        }

        var resp = await _client.DeleteAsync($"/api/mcp/{id}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }
}
