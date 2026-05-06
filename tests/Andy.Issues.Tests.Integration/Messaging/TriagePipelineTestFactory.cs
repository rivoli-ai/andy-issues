// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Andy.Issues.Tests.Integration.Messaging;

// Z12 — extends the standard test factory with `Messaging:ConsumeRunEvents=true`
// so the `ContainerRunEventConsumer` BackgroundService actually runs.
// Most existing tests don't want the consumer subscribed (it would
// silently react to any run.* event published in their scope), so this
// is opt-in per-test-class.
public class TriagePipelineTestFactory : TestWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:ConsumeRunEvents"] = "true",
            });
        });
        base.ConfigureWebHost(builder);
    }
}
