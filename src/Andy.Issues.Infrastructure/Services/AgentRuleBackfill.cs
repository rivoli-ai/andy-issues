using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace Andy.Issues.Infrastructure.Services;

public sealed class AgentRuleBackfill(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await BackfillAsync(db, ct);
    }
    public static async Task BackfillAsync(AppDbContext db, CancellationToken ct = default)
    {
        var repositories = await db.Repositories.Where(x => x.AgentRules != null && x.AgentRules != "" && !db.AgentRules.Any(r => r.RepositoryId == x.Id)).ToListAsync(ct);
        foreach (var repo in repositories)
        {
            await using var mutation = await AgentRuleMutationScope.OpenAsync(db, repo.Id, ct);
            if (!await db.AgentRules.AnyAsync(x => x.RepositoryId == repo.Id, ct))
            {
                db.AgentRules.Add(new AgentRule { Id = Guid.NewGuid(), RepositoryId = repo.Id, Body = repo.AgentRules!, IsDefault = true });
                await db.SaveChangesAsync(ct);
            }
            await mutation.CommitAsync(ct);
        }
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
