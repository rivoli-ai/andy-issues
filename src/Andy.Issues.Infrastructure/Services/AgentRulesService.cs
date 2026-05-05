// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Infrastructure.Services;

public class AgentRulesService : IAgentRulesService
{
    // Mirrors the column-level cap on Repository.AgentRules. Enforced
    // here so the API can return a clean 400 before EF tries to insert
    // the over-sized blob.
    public const int MaxRulesLength = 65536;

    private readonly AppDbContext _db;
    private readonly IRepositoryAccessGuard _guard;
    private readonly IAuditLogService _audit;

    public AgentRulesService(
        AppDbContext db,
        IRepositoryAccessGuard guard,
        IAuditLogService audit)
    {
        _db = db;
        _guard = guard;
        _audit = audit;
    }

    public async Task<(AgentRulesGetOutcome Outcome, AgentRulesDto? Dto)> GetAsync(
        Guid repositoryId,
        string userId,
        CancellationToken ct = default)
    {
        if (!await _guard.CanViewAsync(repositoryId, userId, ct))
            return (AgentRulesGetOutcome.NotFound, null);

        var rules = await _db.Repositories
            .AsNoTracking()
            .Where(r => r.Id == repositoryId)
            .Select(r => r.AgentRules)
            .FirstOrDefaultAsync(ct);

        // Empty-string fallback (not 404) — the contract for #91 is
        // "no rules yet" returns the same shape with a blank body.
        return (AgentRulesGetOutcome.Ok, new AgentRulesDto(rules ?? string.Empty));
    }

    public async Task<AgentRulesUpdateOutcome> UpdateAsync(
        Guid repositoryId,
        string rules,
        string ownerUserId,
        CancellationToken ct = default)
    {
        rules ??= string.Empty;
        if (rules.Length > MaxRulesLength)
            return AgentRulesUpdateOutcome.TooLarge;

        var repo = await _db.Repositories
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
        if (repo is null) return AgentRulesUpdateOutcome.NotFound;
        if (repo.OwnerUserId != ownerUserId) return AgentRulesUpdateOutcome.NotOwner;

        repo.AgentRules = rules.Length == 0 ? null : rules;
        repo.UpdatedAt = DateTimeOffset.UtcNow;

        await _audit.LogAsync(
            userId: ownerUserId,
            action: "RepositoryAgentRulesUpdated",
            resourceType: "Repository",
            resourceId: repositoryId.ToString(),
            details: $"length={rules.Length}",
            ct: ct);

        await _db.SaveChangesAsync(ct);
        return AgentRulesUpdateOutcome.Updated;
    }
}
