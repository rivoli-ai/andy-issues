// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class McpConfigServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public McpConfigServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new AppDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext NewContext() => new(_options);
    private McpConfigService NewService(AppDbContext ctx) => new(ctx);

    [Fact]
    public async Task Create_Stdio_Personal_Succeeds()
    {
        await using var ctx = NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateMcpServerConfigRequest("my-stdio", null, "Stdio",
                Command: "python", ArgumentsJson: "[\"-m\",\"mcp\"]",
                EnvironmentJson: "{\"KEY\":\"v\"}",
                Url: null, HeadersJson: null, IsShared: false),
            "alice", isAdmin: false);

        Assert.Equal(McpConfigOutcome.Ok, result.Outcome);
        Assert.NotNull(result.Dto);
        Assert.True(result.Dto!.Enabled);
        // Secrets are never in the DTO.
        Assert.DoesNotContain("KEY", JsonSerializer.Serialize(result.Dto));
        Assert.True(result.Dto.HasEnvironment);
    }

    [Fact]
    public async Task Create_Remote_MissingUrl_ReturnsInvalid()
    {
        await using var ctx = NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateMcpServerConfigRequest("r", null, "Remote",
                Command: null, ArgumentsJson: null, EnvironmentJson: null,
                Url: null, HeadersJson: null, IsShared: false),
            "alice", isAdmin: false);
        Assert.Equal(McpConfigOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task Create_Stdio_MissingCommand_ReturnsInvalid()
    {
        await using var ctx = NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateMcpServerConfigRequest("r", null, "Stdio",
                Command: null, ArgumentsJson: null, EnvironmentJson: null,
                Url: null, HeadersJson: null, IsShared: false),
            "alice", isAdmin: false);
        Assert.Equal(McpConfigOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task Create_Shared_NonAdmin_Forbidden()
    {
        await using var ctx = NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateMcpServerConfigRequest("shared-one", null, "Stdio",
                Command: "python", ArgumentsJson: null, EnvironmentJson: null,
                Url: null, HeadersJson: null, IsShared: true),
            "alice", isAdmin: false);
        Assert.Equal(McpConfigOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task Create_Shared_Admin_Succeeds()
    {
        await using var ctx = NewContext();
        var result = await NewService(ctx).CreateAsync(
            new CreateMcpServerConfigRequest("shared-two", null, "Remote",
                Command: null, ArgumentsJson: null, EnvironmentJson: null,
                Url: "https://mcp.example.com", HeadersJson: null, IsShared: true),
            "admin", isAdmin: true);
        Assert.Equal(McpConfigOutcome.Ok, result.Outcome);
        Assert.True(result.Dto!.IsShared);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        await using var ctx = NewContext();
        var svc = NewService(ctx);
        await svc.CreateAsync(
            new CreateMcpServerConfigRequest("dup", null, "Stdio",
                Command: "python", ArgumentsJson: null, EnvironmentJson: null,
                Url: null, HeadersJson: null, IsShared: false),
            "alice", isAdmin: false);

        var again = await svc.CreateAsync(
            new CreateMcpServerConfigRequest("dup", null, "Stdio",
                Command: "python", ArgumentsJson: null, EnvironmentJson: null,
                Url: null, HeadersJson: null, IsShared: false),
            "alice", isAdmin: false);
        Assert.Equal(McpConfigOutcome.Conflict, again.Outcome);
    }

    [Fact]
    public async Task ListForUser_ReturnsPersonalPlusShared()
    {
        await using (var ctx = NewContext())
        {
            ctx.McpServerConfigs.AddRange(
                new McpServerConfig { Id = Guid.NewGuid(), Name = "alice-private", OwnerUserId = "alice", Type = McpServerType.Stdio, Command = "py" },
                new McpServerConfig { Id = Guid.NewGuid(), Name = "bob-private", OwnerUserId = "bob", Type = McpServerType.Stdio, Command = "py" },
                new McpServerConfig { Id = Guid.NewGuid(), Name = "team-shared", OwnerUserId = null, IsShared = true, Type = McpServerType.Remote, Url = "https://x" });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        var list = await NewService(ctx2).ListForUserAsync("alice");
        var names = list.Select(l => l.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "alice-private", "team-shared" }, names);
    }

    [Fact]
    public async Task Update_Shared_NonAdmin_Forbidden()
    {
        Guid id;
        await using (var ctx = NewContext())
        {
            var entity = new McpServerConfig { Id = Guid.NewGuid(), Name = "s", IsShared = true, Type = McpServerType.Stdio, Command = "py" };
            ctx.McpServerConfigs.Add(entity);
            await ctx.SaveChangesAsync();
            id = entity.Id;
        }

        await using var ctx2 = NewContext();
        var result = await NewService(ctx2).UpdateAsync(
            id,
            new UpdateMcpServerConfigRequest(Name: "renamed", Description: null, Enabled: null,
                Command: null, ArgumentsJson: null, EnvironmentJson: null, Url: null, HeadersJson: null),
            "alice", isAdmin: false);
        Assert.Equal(McpConfigOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task Update_OtherUsersPersonal_Forbidden()
    {
        Guid id;
        await using (var ctx = NewContext())
        {
            var entity = new McpServerConfig
            {
                Id = Guid.NewGuid(),
                Name = "bobs",
                OwnerUserId = "bob",
                Type = McpServerType.Stdio,
                Command = "py"
            };
            ctx.McpServerConfigs.Add(entity);
            await ctx.SaveChangesAsync();
            id = entity.Id;
        }

        await using var ctx2 = NewContext();
        var result = await NewService(ctx2).UpdateAsync(
            id,
            new UpdateMcpServerConfigRequest(Name: null, Description: null, Enabled: null,
                Command: null, ArgumentsJson: null, EnvironmentJson: null, Url: null, HeadersJson: null),
            "alice", isAdmin: false);
        Assert.Equal(McpConfigOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task Toggle_FlipsEnabled()
    {
        Guid id;
        await using (var ctx = NewContext())
        {
            var entity = new McpServerConfig
            {
                Id = Guid.NewGuid(),
                Name = "t",
                OwnerUserId = "alice",
                Type = McpServerType.Stdio,
                Command = "py",
                Enabled = true
            };
            ctx.McpServerConfigs.Add(entity);
            await ctx.SaveChangesAsync();
            id = entity.Id;
        }

        await using (var ctx = NewContext())
        {
            var r1 = await NewService(ctx).ToggleAsync(id, "alice", isAdmin: false);
            Assert.False(r1.Dto!.Enabled);
            var r2 = await NewService(ctx).ToggleAsync(id, "alice", isAdmin: false);
            Assert.True(r2.Dto!.Enabled);
        }
    }

    [Fact]
    public async Task Delete_NonOwner_ReturnsForbidden()
    {
        Guid id;
        await using (var ctx = NewContext())
        {
            var entity = new McpServerConfig
            {
                Id = Guid.NewGuid(),
                Name = "d",
                OwnerUserId = "alice",
                Type = McpServerType.Stdio,
                Command = "py"
            };
            ctx.McpServerConfigs.Add(entity);
            await ctx.SaveChangesAsync();
            id = entity.Id;
        }

        await using var ctx2 = NewContext();
        var outcome = await NewService(ctx2).DeleteAsync(id, "mallory", isAdmin: false);
        Assert.Equal(McpConfigOutcome.Forbidden, outcome);

        await using var ctx3 = NewContext();
        Assert.NotNull(await ctx3.McpServerConfigs.FindAsync(id));
    }

    [Fact]
    public async Task Delete_Admin_SharedConfig_Succeeds()
    {
        Guid id;
        await using (var ctx = NewContext())
        {
            var entity = new McpServerConfig
            {
                Id = Guid.NewGuid(),
                Name = "shared-del",
                IsShared = true,
                Type = McpServerType.Stdio,
                Command = "py"
            };
            ctx.McpServerConfigs.Add(entity);
            await ctx.SaveChangesAsync();
            id = entity.Id;
        }

        await using var ctx2 = NewContext();
        var outcome = await NewService(ctx2).DeleteAsync(id, "admin", isAdmin: true);
        Assert.Equal(McpConfigOutcome.Ok, outcome);
    }
}
