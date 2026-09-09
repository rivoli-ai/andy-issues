using System.Text.Json.Nodes;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Estimation;
using Andy.Issues.Infrastructure.Messaging.Consumers;
using Andy.Issues.Infrastructure.Messaging.Nats;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.JetStream.Models;
using Xunit;

namespace Andy.Issues.Tests.Integration.Messaging;

public sealed class EstimateTrainingPipelineTests
{
    [NatsFact]
    public async Task TasksProducerFixture_CrossesOwnerStream_ThenTrainsVersionedModel()
    {
        var id = Guid.NewGuid().ToString("N");
        var ownStream = "ISSUES_ESTIMATE_TEST_" + id;
        var tasksStream = "TASKS_ESTIMATE_TEST_" + id;
        var opts = new NatsOptions
        {
            Url = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222",
            StreamName = ownStream,
            StreamSubjects = [$"andy.test-{id}.>"]
        };
        await using var bus = new NatsMessageBus(Options.Create(opts), NullLogger<NatsMessageBus>.Instance);
        await bus.ConnectAsync();
        await bus.JetStream.CreateOrUpdateStreamAsync(new StreamConfig(ownStream, opts.StreamSubjects));
        await bus.JetStream.CreateOrUpdateStreamAsync(new StreamConfig(tasksStream, ["andy.tasks.events.goal.*.estimate_training_sample_recorded"]));
        var path = Path.GetTempFileName();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={path}"));
        services.AddScoped<EstimateTrainingStore>();
        await using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        using var consumer = new EstimateTrainingSampleConsumer(provider.GetRequiredService<IServiceScopeFactory>(), bus,
            NullLogger<EstimateTrainingSampleConsumer>.Instance);
        try
        {
            await consumer.StartAsync(default);
            var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Messaging", "Fixtures", "estimate-training-v3.json"));
            for (var i = 0; i < 10; i++)
            {
                var sample = JsonNode.Parse(json)!.AsObject();
                var goal = Guid.NewGuid();
                sample["goal_id"] = goal.ToString();
                await bus.PublishAsync($"andy.tasks.events.goal.{goal}.estimate_training_sample_recorded", sample, MessageHeaders.NewRoot());
            }
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (true)
            {
                using var scope = provider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (await db.TriageEstimatorSamples.CountAsync() == 10)
                {
                    Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<EstimateTrainingStore>().TrainAsync());
                    var model = await db.TriageEstimatorModels.SingleAsync();
                    Assert.Equal("owner-a", model.TenantId);
                    Assert.Equal(10, model.SampleCount);
                    Assert.Equal(1, model.Version);
                    break;
                }
                Assert.True(DateTimeOffset.UtcNow < deadline, "Training samples did not cross the Tasks stream.");
                await Task.Delay(50);
            }
        }
        finally
        {
            await consumer.StopAsync(default);
            await bus.JetStream.DeleteStreamAsync(tasksStream);
            await bus.JetStream.DeleteStreamAsync(ownStream);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
