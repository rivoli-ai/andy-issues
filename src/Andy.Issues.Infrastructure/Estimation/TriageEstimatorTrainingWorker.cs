using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Estimation;

public sealed class TriageEstimatorTrainingWorker(IServiceScopeFactory scopes, ILogger<TriageEstimatorTrainingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<EstimateTrainingStore>().TrainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Triage model training failed; retrying at next scheduled pass"); }
            var now = DateTimeOffset.UtcNow;
            var next = new DateTimeOffset(now.UtcDateTime.Date.AddHours(2), TimeSpan.Zero);
            if (next <= now) next = next.AddDays(1);
            try { await Task.Delay(next - now, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
