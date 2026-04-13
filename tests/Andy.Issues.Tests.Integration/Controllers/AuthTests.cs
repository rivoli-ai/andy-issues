// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Reflection;
using Andy.Issues.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

/// <summary>
/// Story 8.1 — Verify Andy Auth is the only IdP. All API controllers require
/// <c>[Authorize]</c>; no local auth/login/register endpoints exist.
/// </summary>
public class AuthTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AuthTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void AllControllers_HaveAuthorizeAttribute()
    {
        // Every API controller in the assembly should have [Authorize] at the
        // class level, ensuring Andy Auth is required for all endpoints.
        var assembly = typeof(RepositoriesController).Assembly;
        var controllers = assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase))
                && !t.IsAbstract
                && t.GetCustomAttribute<ApiControllerAttribute>() is not null
                // Controllers explicitly marked [AllowAnonymous] (e.g. HelpController)
                // are intentionally public and excluded from this check.
                && t.GetCustomAttribute<AllowAnonymousAttribute>() is null)
            .ToList();

        Assert.NotEmpty(controllers);

        foreach (var controller in controllers)
        {
            var hasAuthorize = controller.GetCustomAttribute<AuthorizeAttribute>() is not null;
            Assert.True(hasAuthorize,
                $"{controller.Name} is missing [Authorize]. All API controllers must require authentication.");
        }
    }

    [Fact]
    public void NoAuthController_Exists()
    {
        // Story 8.1 requires no local login/register/OAuth callback endpoints.
        var assembly = typeof(RepositoriesController).Assembly;
        var authControllers = assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase))
                && (t.Name.Contains("Auth", StringComparison.OrdinalIgnoreCase)
                    || t.Name.Contains("Login", StringComparison.OrdinalIgnoreCase)
                    || t.Name.Contains("Register", StringComparison.OrdinalIgnoreCase)
                    || t.Name.Contains("Account", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(authControllers);
    }

    [Fact]
    public void NoDomainEntity_HasPasswordField()
    {
        var domainAssembly = typeof(Andy.Issues.Domain.Entities.Repository).Assembly;
        var entities = domainAssembly.GetTypes()
            .Where(t => t.Namespace?.Contains("Entities") == true && t.IsClass)
            .ToList();

        foreach (var entity in entities)
        {
            var passwordProps = entity.GetProperties()
                .Where(p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Empty(passwordProps);
        }
    }

    [Fact]
    public async Task AuthenticatedRequest_ToProtectedEndpoint_Succeeds()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/repositories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_AllowsAnonymous()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
