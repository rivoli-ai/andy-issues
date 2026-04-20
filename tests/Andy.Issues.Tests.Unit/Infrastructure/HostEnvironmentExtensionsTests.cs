// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Infrastructure;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Andy.Issues.Tests.Unit.Infrastructure;

// Guards the split between "dev-only" (Swagger, AllowAll policy) and
// "non-production" (HTTPS metadata bypass) behaviors in Program.cs.
// Mis-classifying a branch was how Conductor shipped with RBAC
// bypassed and developer exception pages enabled.
public class HostEnvironmentExtensionsTests
{
    [Theory]
    [InlineData("Embedded", true)]
    [InlineData("Development", false)]
    [InlineData("Docker", false)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public void IsEmbedded_ReturnsExpected(string envName, bool expected)
    {
        var env = new FakeEnv { EnvironmentName = envName };
        Assert.Equal(expected, env.IsEmbedded());
    }

    [Theory]
    [InlineData("Docker", true)]
    [InlineData("Embedded", false)]
    [InlineData("Development", false)]
    [InlineData("Production", false)]
    public void IsDocker_ReturnsExpected(string envName, bool expected)
    {
        var env = new FakeEnv { EnvironmentName = envName };
        Assert.Equal(expected, env.IsDocker());
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Docker", true)]
    [InlineData("Embedded", true)]
    [InlineData("Production", false)]
    public void IsLocalOrEmbedded_ReturnsTrueForEveryNonProductionEnv(string envName, bool expected)
    {
        var env = new FakeEnv { EnvironmentName = envName };
        Assert.Equal(expected, env.IsLocalOrEmbedded());
    }

    [Fact]
    public void EmbeddedEnvironmentName_MatchesConductorContract()
    {
        // Kept in lock-step with the Swift-side constant
        // `ServiceEnvironment.embeddedEnvironmentName`. If either side
        // drifts, Program.cs's Embedded-specific branches silently
        // become no-ops and the service reverts to Development
        // behaviour inside the shipping desktop app.
        Assert.Equal("Embedded", HostEnvironmentExtensions.EmbeddedEnvironmentName);
    }

    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Andy.Issues.Tests.Unit";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
