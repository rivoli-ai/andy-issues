// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Infrastructure.Services;

public sealed class BacklogGenerationTracker : IBacklogGenerationTracker
{
    private readonly AppDbContext _db;
    private readonly IBoardNotifier _notifier;

    public BacklogGenerationTracker(AppDbContext db, IBoardNotifier? notifier = null)
    {
        _db = db;
        _notifier = notifier ?? new NullBoardNotifier();
    }

    public async Task<BacklogGenerationDto> StartAsync(
        Guid repositoryId,
        string userId,
        CancellationToken ct = default)
    {
        var row = new BacklogGeneration
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            UserId = userId,
            Phase = BacklogGenerationPhase.Pending,
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _db.BacklogGenerations.Add(row);
        await _db.SaveChangesAsync(ct);

        var dto = ToDto(row);
        await _notifier.BacklogGenerationProgressAsync(repositoryId, dto, ct);
        return dto;
    }

    public async Task<BacklogGenerationDto?> AdvanceAsync(
        Guid generationId,
        BacklogGenerationPhase phase,
        string? detail = null,
        CancellationToken ct = default)
    {
        var row = await _db.BacklogGenerations
            .FirstOrDefaultAsync(g => g.Id == generationId, ct);
        if (row is null) return null;

        row.Phase = phase;
        row.Detail = detail;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        if (IsTerminal(phase))
            row.CompletedAt = row.UpdatedAt;

        await _db.SaveChangesAsync(ct);

        var dto = ToDto(row);
        await _notifier.BacklogGenerationProgressAsync(row.RepositoryId, dto, ct);
        return dto;
    }

    public async Task<BacklogGenerationDto?> GetAsync(
        Guid generationId,
        string userId,
        CancellationToken ct = default)
    {
        var row = await _db.BacklogGenerations.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == generationId, ct);
        if (row is null) return null;
        // Owner-scoped — generation rows are visible only to the user
        // who started the run. Repository-share semantics don't grant
        // access to in-flight runs.
        if (row.UserId != userId) return null;
        return ToDto(row);
    }

    private static bool IsTerminal(BacklogGenerationPhase phase) =>
        phase is BacklogGenerationPhase.Completed
              or BacklogGenerationPhase.Failed
              or BacklogGenerationPhase.Cancelled;

    private static BacklogGenerationDto ToDto(BacklogGeneration row) =>
        new(row.Id, row.RepositoryId, row.UserId, row.Phase.ToString(),
            row.Detail, row.StartedAt, row.UpdatedAt, row.CompletedAt);
}
