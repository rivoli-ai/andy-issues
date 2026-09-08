// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Andy.Issues.Api.Auth;
using Andy.Issues.Application.Dtos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

public class AdminUsersControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public AdminUsersControllerTests(TestWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Listing_IsPermissionGatedAndUsesRbacContract(bool admin)
    {
        var handler = new RbacHandler();
        await using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IClaimsTransformation>(new Claims(admin));
            services.AddHttpClient("AndyRbacUsers", http => http.BaseAddress = new Uri("http://rbac/"))
                .ConfigurePrimaryHttpMessageHandler(() => handler);
        }));
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/admin/users?query=alice&role=admin&skip=0&take=25");
        Assert.Equal(admin ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        if (admin)
        {
            var body = await response.Content.ReadFromJsonAsync<AdminUsersDto>();
            Assert.Equal("alice@example.test", Assert.Single(body!.Items).Email);
            Assert.Equal(1, body.Total);
            Assert.Equal("/api/applications/by-code/andy-issues/users?query=alice&role=admin&skip=0&take=25", handler.Path);
            await client.GetAsync("/api/admin/users?query=alice&role=admin&skip=0&take=25");
            Assert.Equal(1, handler.Calls);
            Assert.True(response.Headers.CacheControl!.NoStore);
        }
        else Assert.Equal(0, handler.Calls);
    }

    private sealed class Claims(bool admin) : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (admin) ((ClaimsIdentity)principal.Identity!).AddClaim(new("permission", AdminUsersAuthorization.Permission));
            return Task.FromResult(principal);
        }
    }
    private sealed class RbacHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Path { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Path = request.RequestUri!.PathAndQuery;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { items = new[] { new { userId = Guid.NewGuid(), email = "alice@example.test", displayName = "Alice", roles = new[] { "admin" }, lastSeenAt = (string?)null } }, total = 1, skip = 0, take = 25 })
            });
        }
    }
}
