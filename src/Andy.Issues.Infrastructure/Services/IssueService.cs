// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Application.Messaging.Events;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.ValueTypes;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Infrastructure.Services;

public class IssueService : IIssueService
{
    private readonly AppDbContext _db;

    public IssueService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IssueDto?> GetAsync(Guid id, string userId, CancellationToken ct = default)
    {
        var issue = await _db.Issues.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null) return null;
        if (issue.OwnerUserId != userId) return null;
        return issue.ToDto();
    }

    public async Task<PagedResult<IssueDto>> ListAsync(
        string userId,
        string? triageState,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.Issues.AsNoTracking().Where(i => i.OwnerUserId == userId);

        if (!string.IsNullOrWhiteSpace(triageState))
        {
            // Unknown values yield an empty page rather than throwing —
            // the MCP/CLI surfaces parse case-insensitively and pass the
            // literal through.
            if (Enum.TryParse<TriageState>(triageState, ignoreCase: true, out var parsed))
                query = query.Where(i => i.TriageState == parsed);
            else
                query = query.Where(_ => false);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<IssueDto>(
            items.Select(IssueMapping.ToDto).ToList(),
            page,
            pageSize,
            total);
    }

    public async Task<IssueDto> CreateAsync(CreateIssueRequest request, string userId, CancellationToken ct = default)
    {
        var issue = new Issue
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userId,
            RepositoryId = request.RepositoryId,
            Title = request.Title,
            Body = request.Body
        };
        _db.Issues.Add(issue);
        await _db.SaveChangesAsync(ct);
        return issue.ToDto();
    }

    public Task<IssueTriageResult> StartTriageAsync(Guid id, string userId, CancellationToken ct = default) =>
        TransitionAsync(id, userId, issue => issue.StartTriage(), terminalKind: null, ct);

    public Task<IssueTriageResult> CompleteTriageAsync(Guid id, string userId, TriageOutput? output = null, CancellationToken ct = default) =>
        TransitionAsync(id, userId, issue =>
            {
                issue.CompleteTriage(userId, output);
                // Z5 — append a revision row for every output that
                // arrives. AuthorKind=Agent because CompleteTriage is
                // the path that delivers agent-produced output (Z2's
                // run.finished consumer). Revisions list null outputs
                // skipped — Z1 still allows manual completion without
                // an output payload for testing.
                if (output is not null)
                {
                    _db.TriageOutputRevisions.Add(new TriageOutputRevision
                    {
                        Id = Guid.NewGuid(),
                        IssueId = issue.Id,
                        Author = userId,
                        AuthorKind = TriageRevisionAuthorKind.Agent,
                        TriageOutput = output,
                        DiffSummary = null,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }
            },
            terminalKind: IssueEventKind.Triaged, ct);

    public Task<IssueTriageResult> AcceptAsync(Guid id, string userId, CancellationToken ct = default) =>
        TransitionAsync(id, userId, issue => issue.Accept(userId), terminalKind: IssueEventKind.Accepted, ct,
            isIdempotentTerminal: i => i.TriageState == TriageState.Accepted);

    public Task<IssueTriageResult> RejectAsync(Guid id, string userId, CancellationToken ct = default) =>
        TransitionAsync(id, userId, issue => issue.Reject(userId), terminalKind: IssueEventKind.Rejected, ct,
            isIdempotentTerminal: i => i.TriageState == TriageState.Rejected);

    // Z5 — human edit. EditOutput throws InvalidOperationException
    // if the issue is not Triaged; the shared TransitionAsync helper
    // catches that and returns InvalidTransition (HTTP 409).
    public Task<IssueTriageResult> EditOutputAsync(
        Guid id, string userId, TriageOutput output, string? diffSummary = null, CancellationToken ct = default) =>
        TransitionAsync(id, userId, issue =>
            {
                issue.EditOutput(output, userId);
                _db.TriageOutputRevisions.Add(new TriageOutputRevision
                {
                    Id = Guid.NewGuid(),
                    IssueId = issue.Id,
                    Author = userId,
                    AuthorKind = TriageRevisionAuthorKind.Human,
                    TriageOutput = output,
                    DiffSummary = diffSummary,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            },
            terminalKind: IssueEventKind.Revised, ct);

    public async Task<IssueTriageResult> RevertAsync(
        Guid id, string userId, Guid targetRevisionId, CancellationToken ct = default)
    {
        var target = await _db.TriageOutputRevisions
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == targetRevisionId && r.IssueId == id, ct);
        if (target is null) return IssueTriageResult.NotFound();

        return await EditOutputAsync(id, userId, target.TriageOutput,
            diffSummary: $"Reverted to revision {target.Id}", ct);
    }

    public async Task<IReadOnlyList<TriageOutputRevisionDto>?> ListRevisionsAsync(
        Guid id, string userId, CancellationToken ct = default)
    {
        // Owner-scope check via Issues so revisions don't leak across
        // users even with a guessed Issue id.
        var issue = await _db.Issues.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null) return null;
        if (issue.OwnerUserId != userId) return null;

        var rows = await _db.TriageOutputRevisions.AsNoTracking()
            .Where(r => r.IssueId == id)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(r => new TriageOutputRevisionDto(
            r.Id, r.IssueId, r.Author, r.AuthorKind.ToString(),
            r.TriageOutput, r.DiffSummary, r.CreatedAt)).ToList();
    }

    // Common shape: load issue, run the entity-level transition (which
    // enforces the state machine), append outbox row for terminal
    // transitions, save in one unit of work. `isIdempotentTerminal` is
    // checked BEFORE invoking the transition so a no-op accept/reject
    // does not write a duplicate outbox row.
    private async Task<IssueTriageResult> TransitionAsync(
        Guid id,
        string userId,
        Action<Issue> apply,
        IssueEventKind? terminalKind,
        CancellationToken ct,
        Func<Issue, bool>? isIdempotentTerminal = null)
    {
        var issue = await _db.Issues.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null) return IssueTriageResult.NotFound();
        if (issue.OwnerUserId != userId) return IssueTriageResult.NotFound();

        var idempotent = isIdempotentTerminal?.Invoke(issue) ?? false;

        try
        {
            apply(issue);
        }
        catch (InvalidOperationException ex)
        {
            return IssueTriageResult.InvalidTransition(ex.Message);
        }
        catch (ArgumentException ex)
        {
            // Z3 — invalid TriageOutput payload (e.g. empty rationale).
            // Surfaced as InvalidTransition so REST callers get a 409
            // with the entity's diagnostic message rather than a 500.
            return IssueTriageResult.InvalidTransition(ex.Message);
        }

        if (terminalKind is { } kind && !idempotent)
            _db.AppendIssueEvent(issue, kind);

        await _db.SaveChangesAsync(ct);
        return IssueTriageResult.Ok(issue.ToDto());
    }
}
