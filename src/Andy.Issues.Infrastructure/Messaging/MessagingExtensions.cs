// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Andy.Issues.Infrastructure.Messaging;

public static class MessagingExtensions
{
    // Register the messaging stack: IMessageBus (InMemory today; NATS once
    // Story 15.7 lands), the OutboxDispatcherOptions binding, and the
    // OutboxDispatcher hosted service. Program.cs calls this once after
    // AddAppDatabase so the dispatcher's AppDbContext dependency resolves.
    public static IServiceCollection AddIssuesMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Messaging:Provider"] ?? "InMemory";

        // Story 15.7 will add the "Nats" branch that configures NatsOptions
        // and swaps the IMessageBus singleton. Until then InMemory is the
        // only supported choice and an unknown value logs a warning-worthy
        // misconfig; we treat unknown as InMemory for backwards-safety.
        _ = provider; // reserved for 15.7
        services.AddSingleton<IMessageBus, InMemoryMessageBus>();

        services.Configure<OutboxDispatcherOptions>(
            configuration.GetSection(OutboxDispatcherOptions.SectionName));
        services.AddHostedService<OutboxDispatcher>();

        return services;
    }
}
