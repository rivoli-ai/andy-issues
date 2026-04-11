// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Tests.Integration.Fakes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Andy.Issues.Tests.Integration;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"andy-issues-tests-{Guid.NewGuid()}";
    public FakeGitHubClient FakeGitHubClient { get; } = new();
    public FakeAzureDevOpsClient FakeAzureDevOpsClient { get; } = new();
    public FakeContainersClient FakeContainersClient { get; } = new();
    public FakePermissionChecker PermissionCheckerFake { get; } = new();
    public FakeMcpToolDiscoveryClient FakeMcpToolDiscoveryClient { get; } = new();

    public void ResetPermissions() => PermissionCheckerFake.Reset();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AndyAuth:Authority"] = "",
                ["ConnectionStrings:DefaultConnection"] = "",
            });
        });

        builder.ConfigureServices(services =>
        {
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbDescriptor is not null)
                services.Remove(dbDescriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            // Replace the real external clients with fakes whose responses
            // are seeded from individual tests.
            var ghDescriptor = services.FirstOrDefault(
                d => d.ServiceType == typeof(IGitHubClient));
            if (ghDescriptor is not null)
                services.Remove(ghDescriptor);
            services.AddSingleton<IGitHubClient>(FakeGitHubClient);

            var azDescriptor = services.FirstOrDefault(
                d => d.ServiceType == typeof(IAzureDevOpsClient));
            if (azDescriptor is not null)
                services.Remove(azDescriptor);
            services.AddSingleton<IAzureDevOpsClient>(FakeAzureDevOpsClient);

            var containersDescriptor = services.FirstOrDefault(
                d => d.ServiceType == typeof(IContainersClient));
            if (containersDescriptor is not null)
                services.Remove(containersDescriptor);
            services.AddSingleton<IContainersClient>(FakeContainersClient);

            var permissionDescriptor = services.FirstOrDefault(
                d => d.ServiceType == typeof(IPermissionChecker));
            if (permissionDescriptor is not null)
                services.Remove(permissionDescriptor);
            services.AddSingleton<IPermissionChecker>(PermissionCheckerFake);

            var discoveryDescriptor = services.FirstOrDefault(
                d => d.ServiceType == typeof(IMcpToolDiscoveryClient));
            if (discoveryDescriptor is not null)
                services.Remove(discoveryDescriptor);
            services.AddSingleton<IMcpToolDiscoveryClient>(FakeMcpToolDiscoveryClient);

            // Replace whatever auth Program.cs wired up with a test handler that
            // always authenticates as `dev-user`. This is independent of the
            // AndyAuth:Authority config path, which isn't reliably overridable
            // before Program.cs reads it.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder(TestAuthHandler.SchemeName)
                    .RequireAuthenticatedUser()
                    .Build();
            });
        });
    }
}
