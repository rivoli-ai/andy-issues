// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using Andy.Issues.Api.Telemetry;
using Andy.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Unit.Telemetry;

/// <summary>
/// OT5 (rivoli-ai/conductor#1263). Asserts that the andy-issues service:
///   1. Calls <see cref="AndyTelemetryExtensions.AddAndyTelemetry"/> from the
///      shared library without throwing.
///   2. Registers the domain <see cref="IssuesTelemetry.ActivitySource"/>
///      so an <see cref="ActivityListener"/> subscribed to that name receives
///      spans (regression guard against accidental rename in Program.cs).
/// </summary>
public class AndyTelemetryAdoptionTests
{
    [Fact]
    public void AddAndyTelemetry_does_not_throw_when_minimal_config_is_provided()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AndyTelemetry:ServiceName"] = "andy-issues",
            })
            .Build();

        var exception = Record.Exception(() => services.AddAndyTelemetry(configuration, o =>
        {
            o.ActivitySources.Add(IssuesTelemetry.ActivitySourceName);
            o.Meters.Add(IssuesTelemetry.MeterName);
            o.EnableAspNetCoreInstrumentation = false;
            o.EnableHttpClientInstrumentation = true;
        }));

        Assert.Null(exception);
    }

    [Fact]
    public void AddAndyTelemetry_persists_options_with_otlp_endpoint()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AndyTelemetry:ServiceName"] = "andy-issues",
                ["AndyTelemetry:OtlpEndpoint"] = "http://localhost:4318",
                ["AndyTelemetry:Protocol"] = "http/protobuf",
            })
            .Build();

        services.AddAndyTelemetry(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AndyTelemetryOptions>();

        Assert.Equal("andy-issues", options.ServiceName);
        Assert.Equal("http://localhost:4318", options.OtlpEndpoint);
        Assert.Equal("http/protobuf", options.Protocol);
    }

    [Fact]
    public void IssuesActivitySource_emits_when_listened_to()
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == IssuesTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => captured.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        using (var activity = IssuesTelemetry.ActivitySource.StartActivity("BacklogSync"))
        {
            Assert.NotNull(activity);
            activity!.SetTag("backlog.repo", "test/repo");
        }

        Assert.Single(captured);
        Assert.Equal("BacklogSync", captured[0].OperationName);
        Assert.Equal("test/repo", captured[0].GetTagItem("backlog.repo"));
    }
}
