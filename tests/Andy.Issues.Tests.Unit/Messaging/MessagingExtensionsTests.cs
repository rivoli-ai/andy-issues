// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Messaging;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Messaging;
using Andy.Issues.Infrastructure.Messaging.Nats;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Andy.Issues.Tests.Unit.Messaging;

// Covers the branch selection in AddIssuesMessaging — Story 15.7.
// Full NATS round-trip coverage lives in the env-gated integration
// suite (Story 15.8).
public class MessagingExtensionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("InMemory")]
    [InlineData("inmemory")]
    [InlineData("bogus-provider-name")]  // unknown falls back to InMemory
    public void AddIssuesMessaging_DefaultsToInMemory(string? provider)
    {
        var sp = BuildProvider(provider);
        var bus = sp.GetRequiredService<IMessageBus>();
        Assert.IsType<InMemoryMessageBus>(bus);
    }

    [Fact]
    public void AddIssuesMessaging_NatsProvider_ResolvesNatsBus()
    {
        var sp = BuildProvider("Nats");
        var bus = sp.GetRequiredService<IMessageBus>();
        Assert.IsType<NatsMessageBus>(bus);

        // NatsOptions should bind from Messaging:Nats section with defaults.
        var options = sp.GetRequiredService<IOptions<NatsOptions>>().Value;
        Assert.Equal("nats://localhost:4222", options.Url);
        Assert.Equal("ANDY", options.StreamName);
        Assert.Equal("andy.issues.dlq", options.DlqPrefix);
    }

    [Fact]
    public void AddIssuesMessaging_NatsProvider_CaseInsensitive()
    {
        var sp = BuildProvider("nats");
        Assert.IsType<NatsMessageBus>(sp.GetRequiredService<IMessageBus>());
    }

    [Fact]
    public void AddIssuesMessaging_NatsProvider_BindsCustomUrl()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:Provider"] = "Nats",
                ["Messaging:Nats:Url"] = "nats://remote.example:4222",
                ["Messaging:Nats:DlqPrefix"] = "custom.dlq"
            })
            .Build();

        var sp = BuildProviderFromConfig(config);
        var options = sp.GetRequiredService<IOptions<NatsOptions>>().Value;

        Assert.Equal("nats://remote.example:4222", options.Url);
        Assert.Equal("custom.dlq", options.DlqPrefix);
    }

    private static ServiceProvider BuildProvider(string? provider)
    {
        var items = new Dictionary<string, string?>();
        if (provider is not null)
            items["Messaging:Provider"] = provider;
        var config = new ConfigurationBuilder().AddInMemoryCollection(items).Build();
        return BuildProviderFromConfig(config);
    }

    private static ServiceProvider BuildProviderFromConfig(IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // AppDbContext is required by OutboxDispatcher; use in-memory.
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase($"msg-ext-{Guid.NewGuid()}"));
        services.AddIssuesMessaging(config);
        return services.BuildServiceProvider();
    }
}
