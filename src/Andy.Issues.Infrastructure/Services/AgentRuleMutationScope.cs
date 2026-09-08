using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace Andy.Issues.Infrastructure.Services;

// All profile writes and selections share the repository lock, including embedded databases
// healed from older schemas where the additive story column cannot gain a foreign key.
internal sealed class AgentRuleMutationScope(SemaphoreSlim gate, IDbContextTransaction? transaction) : IAsyncDisposable
{
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    public static async Task<AgentRuleMutationScope> OpenAsync(AppDbContext db, Guid repositoryId, CancellationToken ct)
    {
        var gate = Gates[(uint)repositoryId.GetHashCode() % (uint)Gates.Length];
        await gate.WaitAsync(ct);
        IDbContextTransaction? tx = null;
        try
        {
            if (db.Database.IsRelational()) tx = await db.Database.BeginTransactionAsync(ct);
            if (db.Database.IsNpgsql())
                await db.Repositories.FromSqlInterpolated($"SELECT * FROM \"Repositories\" WHERE \"Id\" = {repositoryId} FOR UPDATE").LoadAsync(ct);
            return new(gate, tx);
        }
        catch { if (tx is not null) await tx.DisposeAsync(); gate.Release(); throw; }
    }
    public Task CommitAsync(CancellationToken ct) => transaction?.CommitAsync(ct) ?? Task.CompletedTask;
    public async ValueTask DisposeAsync()
    {
        try { if (transaction is not null) await transaction.DisposeAsync(); }
        finally { gate.Release(); }
    }
}
