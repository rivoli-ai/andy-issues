// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Api.Infrastructure;

/// <summary>Protect old plaintext rows before accepting traffic. Legacy Settings refs remain refs.</summary>
public sealed class LlmKeyMigration(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var secrets = scope.ServiceProvider.GetRequiredService<ILlmSecretStore>();
        var rows = await db.LlmSettings.Where(s => s.ApiKey != "" && !s.ApiKey.StartsWith("secret::")
            && !s.ApiKey.StartsWith(LlmSecretStore.Prefix)).ToListAsync(ct);
        foreach (var row in rows) row.ApiKey = await secrets.StoreAsync(row.Id.ToString(), row.ApiKey, ct);
        await db.SaveChangesAsync(ct);
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
