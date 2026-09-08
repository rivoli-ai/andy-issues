// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Api.Infrastructure;

public sealed class RecategorizationWorker(IServiceScopeFactory scopes, ILogger<RecategorizationWorker> logger) : BackgroundService
{
    private readonly SemaphoreSlim _slots = new(32, 32);
    private readonly Channel<Work> _queue = Channel.CreateBounded<Work>(32);
    private readonly ConcurrentDictionary<Guid, byte> _repositories = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private record Work(Guid Id, Guid RepositoryId, string UserId, bool ApplyToGitHub, ClaimsPrincipal User, string Authorization);

    public async Task<RecategorizationJob?> EnqueueAsync(Guid repositoryId, string userId, bool applyToGitHub,
        HttpContext context, CancellationToken ct)
    {
        if (!_slots.Wait(0)) return null;
        if (!_repositories.TryAdd(repositoryId, 0)) { _slots.Release(); return null; }
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = new RecategorizationJob { Id = Guid.NewGuid(), RepositoryId = repositoryId, UserId = userId };
            db.RecategorizationJobs.Add(row);
            AppendProgress(db, row);
            await db.SaveChangesAsync(ct);
            var work = new Work(row.Id, repositoryId, userId, applyToGitHub,
                context.User.Clone(), context.Request.Headers.Authorization.ToString());
            if (!_queue.Writer.TryWrite(work))
            {
                db.RecategorizationJobs.Remove(row);
                await db.SaveChangesAsync(ct);
                _repositories.TryRemove(repositoryId, out _);
                _slots.Release();
                return null;
            }
            return row;
        }
        catch
        {
            _repositories.TryRemove(repositoryId, out _);
            _slots.Release();
            throw;
        }
    }

    public override async Task StartAsync(CancellationToken stoppingToken)
    {
        // Startup database initialization runs in Program before hosted services start.
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var interrupted = await db.RecategorizationJobs.Where(j => j.CompletedAt == null).ToListAsync(stoppingToken);
            foreach (var row in interrupted)
            {
                row.Phase = "Failed";
                row.Error = "Service restarted; submit recategorization again.";
                row.CompletedAt = row.UpdatedAt = DateTimeOffset.UtcNow;
                AppendProgress(db, row);
            }
            await db.SaveChangesAsync(stoppingToken);
        }

        await base.StartAsync(stoppingToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
                // Snapshot only the caller identity/bearer, never the disposed request or its cancellation token.
                // The bearer stays in memory for OBO and is never persisted with the job.
                accessor.HttpContext = new DefaultHttpContext { User = work.User, RequestServices = scope.ServiceProvider };
                accessor.HttpContext.Request.Headers.Authorization = work.Authorization;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromMinutes(15));
                    var service = scope.ServiceProvider.GetRequiredService<IBacklogRecategorizeService>();
                    var result = await service.RecategorizeAsync(work.RepositoryId, work.UserId, work.ApplyToGitHub,
                        timeout.Token, phase => AdvanceAsync(work, phase, null, null, timeout.Token));
                    var success = result?.Outcome is RecategorizeOutcome.Recategorized or RecategorizeOutcome.NothingToDo;
                    await AdvanceAsync(work, success ? "Completed" : "Failed", result,
                        success ? null : result?.Message ?? "Repository is no longer accessible.", CancellationToken.None);
                }
                finally { accessor.HttpContext = null; }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Recategorization job {JobId} failed ({ErrorType}).", work.Id, ex.GetType().Name);
                try
                {
                    await AdvanceAsync(work, "Failed", null,
                        ex is OperationCanceledException ? "Recategorization interrupted or timed out." : "Recategorization failed.", CancellationToken.None);
                }
                catch (Exception persistenceError)
                {
                    logger.LogError("Could not persist failed job {JobId} ({ErrorType}). Startup recovery will mark it interrupted.",
                        work.Id, persistenceError.GetType().Name);
                }
            }
            finally { _repositories.TryRemove(work.RepositoryId, out _); _slots.Release(); }
        }
    }

    private async Task AdvanceAsync(Work work, string phase, RecategorizeResult? result, string? error, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.RecategorizationJobs.FindAsync(new object[] { work.Id }, ct);
        if (row is null) return;
        row.Phase = phase;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.Error = error;
        if (result is not null) row.ResultJson = JsonSerializer.Serialize(result, JsonOptions);
        if (phase is "Completed" or "Failed") row.CompletedAt = row.UpdatedAt;
        AppendProgress(db, row);
        await db.SaveChangesAsync(ct);
        try
        {
            await scope.ServiceProvider.GetRequiredService<IBoardNotifier>().BacklogGenerationProgressAsync(
                work.RepositoryId, new BacklogGenerationDto(row.Id, row.RepositoryId, row.UserId, phase,
                    error, row.StartedAt, row.UpdatedAt, row.CompletedAt), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Progress notification failed for job {JobId} ({ErrorType}); polling remains available.", work.Id, ex.GetType().Name);
        }
    }
    private static void AppendProgress(AppDbContext db, RecategorizationJob job)
    {
        db.Outbox.Add(new OutboxEntry
        {
            Id = Guid.NewGuid(),
            Subject = $"andy.issues.events.recategorization.{job.Id}.progress",
            CorrelationId = job.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                schema_version = 1,
                job_id = job.Id,
                repository_id = job.RepositoryId,
                phase = job.Phase,
                updated_at = job.UpdatedAt,
                completed_at = job.CompletedAt
            })
        });
    }

}
