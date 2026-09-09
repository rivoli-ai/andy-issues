using System.Text.Json;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Infrastructure.Estimation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Messaging.Consumers;

public sealed class EstimateTrainingSampleConsumer(IServiceScopeFactory scopes, IMessageBus bus,
    ILogger<EstimateTrainingSampleConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var message in bus.SubscribeAsync("andy.tasks.events.goal.*.estimate_training_sample_recorded",
                    new SubscriptionOptions("andy-issues-estimate-training-v1", DiscoverStream: true), stoppingToken))
                {
                    try
                    {
                        using var scope = scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<EstimateTrainingStore>().RecordAsync(message.Payload, stoppingToken);
                        await message.AckAsync(stoppingToken);
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "Invalid estimate sample {MessageId}; ignoring", message.Headers.MsgId);
                        await message.AckAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Estimate sample persistence failed; retrying {MessageId}", message.Headers.MsgId);
                        await message.NackAsync(stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Estimate training subscription unavailable; retrying in 5 seconds");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
