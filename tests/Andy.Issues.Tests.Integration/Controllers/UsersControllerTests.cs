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

public class UsersControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UsersControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.UserDirectory.Any(u => u.UserId == "target-suggest"))
        {
            db.UserDirectory.Add(new UserDirectoryEntry
            {
                Id = Guid.NewGuid(),
                UserId = "target-suggest",
                Email = "target@example.com",
                DisplayName = "Target"
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Suggest_EmptyQuery_ReturnsEmpty()
    {
        var response = await _client.GetAsync("/api/users/suggest?q=");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<UserSuggestionDto>>(JsonOptions);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Suggest_MatchingPrefix_ReturnsResult()
    {
        await SeedAsync();
        var response = await _client.GetAsync("/api/users/suggest?q=target");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await response.Content.ReadFromJsonAsync<List<UserSuggestionDto>>(JsonOptions);
        Assert.Contains(list!, u => u.UserId == "target-suggest");
    }
}
