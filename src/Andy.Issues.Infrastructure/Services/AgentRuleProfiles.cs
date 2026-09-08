using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Infrastructure.Services;

public sealed class AgentRuleProfiles(AppDbContext db, IRepositoryAccessGuard guard,
    IAuditLogService audit, IAndySettingsClient settings, IBoardNotifier notifier) : IAgentRuleProfiles
{
    private static AgentRuleProfileDto Dto(AgentRule x) => new(x.Id, x.RepositoryId, x.Name, x.Body, x.IsDefault, x.SortOrder, x.CreatedAt, x.UpdatedAt);
    public async Task<IReadOnlyList<AgentRuleProfileDto>?> ListAsync(Guid repositoryId, string userId, CancellationToken ct = default)
    {
        if (!await guard.CanViewAsync(repositoryId, userId, ct)) return null;
        return (await db.AgentRules.AsNoTracking().Where(x => x.RepositoryId == repositoryId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct)).Select(Dto).ToList();
    }

    public async Task<IReadOnlyList<AgentRuleProfileDto>?> WriteAsync(Guid repositoryId, string userId, string operation,
        AgentRuleWriteRequest? request = null, Guid? ruleId = null, IReadOnlyList<AgentRuleWriteRequest>? replacement = null, CancellationToken ct = default)
    {
        await using var mutation = await AgentRuleMutationScope.OpenAsync(db, repositoryId, ct);
        {
            var repo = await db.Repositories.SingleOrDefaultAsync(x => x.Id == repositoryId, ct);
            if (repo is null || repo.OwnerUserId != userId) return null;
            var old = await db.AgentRules.Where(x => x.RepositoryId == repositoryId).ToListAsync(ct);
            var desired = old.Select(x => new AgentRuleWriteRequest(x.Name, x.Body, x.IsDefault, x.SortOrder, x.Id)).ToList();
            // Also covers legacy writes/imports after startup.
            if (desired.Count == 0 && !string.IsNullOrEmpty(repo.AgentRules))
                desired.Add(new("Default", repo.AgentRules, true, 0, Guid.NewGuid()));
            switch (operation)
            {
                case "create": desired.Add(request! with { Id = Guid.NewGuid() }); break;
                case "update":
                    var index = desired.FindIndex(x => x.Id == ruleId);
                    if (index < 0) return null;
                    desired[index] = request! with { Id = ruleId }; break;
                case "delete":
                    if (desired.RemoveAll(x => x.Id == ruleId) == 0) return null;
                    break;
                case "default":
                    if (!desired.Any(x => x.Id == ruleId)) return null;
                    desired = desired.Select(x => x with { IsDefault = x.Id == ruleId }).ToList(); break;
                case "legacy":
                    var current = desired.FindIndex(x => x.IsDefault);
                    if (current < 0) desired.Add(new("Default", request!.Body, true, 0, Guid.NewGuid()));
                    else desired[current] = desired[current] with { Body = request!.Body };
                    break;
                case "replace":
                    desired = (replacement ?? []).Select(x => x with { Id = x.Id ?? Guid.NewGuid() }).ToList();
                    if (desired.Any(x => replacement!.Any(r => r.Id == x.Id) && !old.Any(o => o.Id == x.Id)))
                        throw new AgentRuleValidationException("A profile ID does not belong to this repository.");
                    break;
                default: throw new ArgumentException("Unknown rule operation.", nameof(operation));
            }
            if (request?.IsDefault == true && operation is "create" or "update")
            {
                var selected = operation == "create" ? desired[^1].Id : ruleId;
                desired = desired.Select(x => x with { IsDefault = x.Id == selected }).ToList();
            }
            desired = desired.Select(x => x with { Name = (x.Name ?? "").Trim(), Body = x.Body ?? "" }).ToList();
            if (desired.Count > 100) throw new AgentRuleValidationException("A repository supports at most 100 profiles.");
            if (desired.Any(x => x.Name.Length is 0 or > 120 || x.Body.Length > 65536))
                throw new AgentRuleValidationException("Names must contain 1–120 characters; rule bodies cannot exceed 65536 characters.");
            if (desired.Select(x => x.Id).Distinct().Count() != desired.Count ||
                desired.Select(x => x.Name.ToUpperInvariant()).Distinct().Count() != desired.Count)
                throw new AgentRuleValidationException("Profile IDs and names must be unique within the repository.");
            if (desired.Count(x => x.IsDefault) > 1) throw new AgentRuleValidationException("Only one profile can be the default.");
            if (desired.Count > 0 && !desired.Any(x => x.IsDefault))
            {
                var selected = desired.OrderByDescending(x => x.SortOrder).ThenBy(x => x.Id).First().Id;
                desired = desired.Select(x => x with { IsDefault = x.Id == selected }).ToList();
            }
            // Release unique keys inside the transaction to allow atomic default/name swaps.
            foreach (var x in old) { x.IsDefault = false; x.NameKey = Guid.NewGuid().ToString("N"); }
            await db.SaveChangesAsync(ct);
            var removed = old.Where(x => !desired.Any(d => d.Id == x.Id)).ToList();
            var removedIds = removed.Select(x => x.Id).ToList();
            // Explicit clearing supports healed SQLite databases whose added columns lack FK constraints.
            foreach (var story in await db.UserStories.Where(x => x.AgentRuleId != null && removedIds.Contains(x.AgentRuleId.Value)).ToListAsync(ct))
                story.AgentRuleId = null;
            db.AgentRules.RemoveRange(removed);
            var result = new List<AgentRule>();
            foreach (var value in desired)
            {
                var entity = old.FirstOrDefault(x => x.Id == value.Id);
                if (entity is null) { entity = new AgentRule { Id = value.Id!.Value, RepositoryId = repositoryId }; db.AgentRules.Add(entity); }
                entity.Name = value.Name; entity.NameKey = value.Name.ToUpperInvariant(); entity.Body = value.Body;
                entity.IsDefault = value.IsDefault; entity.SortOrder = value.SortOrder; entity.UpdatedAt = DateTimeOffset.UtcNow;
                result.Add(entity);
            }
            var defaultBody = result.FirstOrDefault(x => x.IsDefault)?.Body;
            repo.AgentRules = string.IsNullOrEmpty(defaultBody) ? null : defaultBody;
            repo.UpdatedAt = DateTimeOffset.UtcNow;
            await audit.LogAsync(userId, "RepositoryAgentRulesUpdated", "Repository", repositoryId.ToString(), operation == "legacy" ? $"length={request!.Body.Length}" : $"operation={operation}; profiles={result.Count}", ct);
            await db.SaveChangesAsync(ct);
            await mutation.CommitAsync(ct);
            return result.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).Select(Dto).ToList();
        }
    }

    public async Task<bool> SetDefaultAsync(Guid ruleId, string userId, CancellationToken ct = default)
    {
        var repoId = await db.AgentRules.Where(x => x.Id == ruleId).Select(x => (Guid?)x.RepositoryId).SingleOrDefaultAsync(ct);
        return repoId is not null && await WriteAsync(repoId.Value, userId, "default", ruleId: ruleId, ct: ct) is not null;
    }
    public async Task<bool> SelectAsync(Guid storyId, Guid? ruleId, string userId, CancellationToken ct = default)
    {
        var story = await db.UserStories.Include(x => x.Feature).ThenInclude(x => x.Epic).SingleOrDefaultAsync(x => x.Id == storyId, ct);
        if (story is null || !await guard.CanViewAsync(story.Feature.Epic.RepositoryId, userId, ct)) return false;
        await using var mutation = await AgentRuleMutationScope.OpenAsync(db, story.Feature.Epic.RepositoryId, ct);
        if (ruleId is not null && !await db.AgentRules.AnyAsync(x => x.Id == ruleId && x.RepositoryId == story.Feature.Epic.RepositoryId, ct))
            throw new AgentRuleValidationException("The selected profile must belong to the story's repository.");
        story.AgentRuleId = ruleId;
        story.UpdatedAt = DateTimeOffset.UtcNow;
        await audit.LogAsync(userId, "StoryAgentRuleSelected", "UserStory", storyId.ToString(), $"ruleId={ruleId}", ct);
        await db.SaveChangesAsync(ct);
        await mutation.CommitAsync(ct);
        await notifier.StoryUpdatedAsync(story.Feature.Epic.RepositoryId, Andy.Issues.Application.Mapping.BacklogMapping.ToDto(story), ct);
        return true;
    }
    public async Task<EffectiveAgentRulesDto?> EffectiveAsync(Guid storyId, string userId, CancellationToken ct = default)
    {
        var story = await db.UserStories.AsNoTracking().Include(x => x.Feature).ThenInclude(x => x.Epic).SingleOrDefaultAsync(x => x.Id == storyId, ct);
        if (story is null || !await guard.CanViewAsync(story.Feature.Epic.RepositoryId, userId, ct)) return null;
        var profiles = await db.AgentRules.AsNoTracking().Where(x => x.RepositoryId == story.Feature.Epic.RepositoryId).ToListAsync(ct);
        var selected = profiles.FirstOrDefault(x => x.Id == story.AgentRuleId);
        var profile = selected ?? profiles.FirstOrDefault(x => x.IsDefault);
        if (profile is not null) return new(profile.Body, selected is null ? "repository-default" : "story", profile.Id, profile.Name);
        var legacy = await db.Repositories.Where(x => x.Id == story.Feature.Epic.RepositoryId).Select(x => x.AgentRules).SingleAsync(ct);
        if (!string.IsNullOrEmpty(legacy)) return new(legacy, "repository-default", null, "Default");
        return new(await settings.GetAsync<string>("andy-issues:agent-rules:system-default", ct) ?? "", "system-default", null, null);
    }
}
