// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Services;

public class AzureDevOpsBacklogPullJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AzureDevOpsBacklogPullJob> _logger;

    public AzureDevOpsBacklogPullJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<AzureDevOpsBacklogPullJob> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = GetInterval();
        if (interval <= TimeSpan.Zero)
        {
            _logger.LogInformation("AzureDevOpsBacklogPullJob disabled (interval <= 0).");
            return;
        }

        _logger.LogInformation("AzureDevOpsBacklogPullJob starting with interval {Interval}.", interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AzureDevOpsBacklogPullJob tick failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sync = scope.ServiceProvider.GetRequiredService<IBacklogAzureDevOpsSyncService>();

        var candidates = await db.Repositories
            .AsNoTracking()
            .Where(r => r.Provider == RepositoryProvider.AzureDevOps)
            .Select(r => new { r.Id, r.OwnerUserId })
            .ToListAsync(ct);

        foreach (var repo in candidates)
        {
            if (ct.IsCancellationRequested) break;
            var result = await sync.PullAsync(repo.Id, repo.OwnerUserId, ct);
            if (result is { Updated: > 0 })
            {
                _logger.LogInformation(
                    "Azure DevOps pull for repo {RepoId}: {Updated} story updates applied.",
                    repo.Id, result.Updated);
            }
        }
    }

    private TimeSpan GetInterval()
    {
        var seconds = _config.GetValue<int?>("Andy:Issues:AzureDevops:PullIntervalSeconds") ?? 0;
        return seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
    }
}
