// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Messaging;
using Andy.Issues.Infrastructure.Messaging.Consumers;
using Andy.Issues.Infrastructure.Messaging.Nats;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Andy.Issues.Infrastructure.Messaging;

public static class MessagingExtensions
{
    // Register the messaging stack: IMessageBus (InMemory by default,
    // Nats when Messaging:Provider=Nats), the OutboxDispatcherOptions
    // binding, and the OutboxDispatcher hosted service. Program.cs
    // calls this once after AddAppDatabase so the dispatcher's
    // AppDbContext dependency resolves.
    public static IServiceCollection AddIssuesMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Messaging:Provider"] ?? "InMemory";

        if (string.Equals(provider, "Nats", StringComparison.OrdinalIgnoreCase))
        {
            services.Configure<NatsOptions>(
                configuration.GetSection(NatsOptions.SectionName));
            services.AddSingleton<NatsMessageBus>();
            services.AddSingleton<IMessageBus>(sp => sp.GetRequiredService<NatsMessageBus>());
            services.AddHostedService<NatsStreamProvisioner>();
        }
        else
        {
            // InMemory is the default and the local-dev fallback. An
            // unknown value for Messaging:Provider is treated as
            // InMemory to avoid boot failures on typos.
            services.AddSingleton<IMessageBus, InMemoryMessageBus>();
        }

        services.Configure<OutboxDispatcherOptions>(
            configuration.GetSection(OutboxDispatcherOptions.SectionName));
        services.AddHostedService<OutboxDispatcher>();

        // Consumers (ADR 0001). Always-on per AK4 — selective disable
        // in incidents goes through `nats consumer pause`, not config.
        services.AddHostedService<ContainerRunEventConsumer>();
        // AH6 (rivoli-ai/conductor#713): reverse-pin the
        // andy-tasks Goal back-reference onto the originating Issue
        // when SourceIssueDisplayId carries an ISSUE-N form.
        services.AddHostedService<GoalLinkageConsumer>();

        return services;
    }
}
