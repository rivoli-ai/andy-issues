// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Microsoft.Extensions.Hosting;

namespace Andy.Issues.Api.Infrastructure;

/// <summary>
/// Named accessors for the three deployment modes every Andy service
/// supports. See andy-service-template/docs/ports.md for the canonical
/// description of the modes and their port ranges.
///
/// Duplicated (by design) across every andy-* service — no shared
/// library today. Keep the string constants in lock-step with the
/// Swift-side definition in
/// <c>Conductor/Core/ServiceHost/ServiceEnvironment.swift</c>.
/// </summary>
public static class HostEnvironmentExtensions
{
    public const string EmbeddedEnvironmentName = "Embedded";
    public const string DockerEnvironmentName = "Docker";

    /// <summary>
    /// True when running inside Conductor's bundled service host.
    /// </summary>
    public static bool IsEmbedded(this IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.IsEnvironment(EmbeddedEnvironmentName);
    }

    /// <summary>
    /// True when running inside a docker-compose stack.
    /// </summary>
    public static bool IsDocker(this IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.IsEnvironment(DockerEnvironmentName);
    }

    /// <summary>
    /// True for any non-production mode (Development, Docker, Embedded).
    /// Use for behaviours safe across every local-development shape —
    /// e.g. disabling HTTPS metadata requirement when talking to
    /// andy-auth through a plain-HTTP local proxy. Do NOT use for
    /// leaky behaviours (Swagger, dev exception pages, permission
    /// bypass) — those stay gated off <see cref="IHostEnvironment.IsDevelopment"/>
    /// so the shipping Conductor app does not expose them.
    /// </summary>
    public static bool IsLocalOrEmbedded(this IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return !environment.IsProduction();
    }
}
