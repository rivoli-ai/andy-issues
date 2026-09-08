namespace Andy.Issues.Infrastructure.Services;

internal sealed class IssueMutationScope(SemaphoreSlim gate) : IDisposable
{
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    public static async Task<IssueMutationScope> OpenAsync(Guid id, CancellationToken ct)
    {
        var gate = Gates[(uint)id.GetHashCode() % (uint)Gates.Length]; await gate.WaitAsync(ct); return new(gate);
    }
    public void Dispose() => gate.Release();
}
