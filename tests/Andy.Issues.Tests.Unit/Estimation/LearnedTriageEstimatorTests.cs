using System.Text.Json;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Estimation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Estimation;

public sealed class LearnedTriageEstimatorTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private AppDbContext _db = null!;
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
    }
    public async Task DisposeAsync() { await _db.DisposeAsync(); await _connection.DisposeAsync(); }

    [Fact]
    public void Defaults_CanBeOverriddenWithoutAllowingInvalidPercentiles()
    {
        var values = new Dictionary<string, string?>
        {
            ["Estimation:Defaults:Templates:BugFix:CostP50"] = "10",
            ["Estimation:Defaults:Templates:BugFix:CostP90"] = "20",
            ["Estimation:Defaults:SeverityMultipliers:Moderate"] = "2",
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.Equal(20, new TriageEstimator(config).Estimate("tenant", TriageTemplateId.BugFix, TriageSeverity.Moderate).CostP50);
        config["Estimation:Defaults:Templates:BugFix:CostP90"] = "-1";
        Assert.Throws<InvalidOperationException>(() => new TriageEstimator(config));
    }

    [Fact]
    public async Task Promotion_RequiresTenValidSamplesAndAuthoritativeCompletions_ForThisTenantTemplate()
    {
        var store = new EstimateTrainingStore(_db);
        var count = new Counts { Value = 100 };
        var estimator = new LearnedTriageEstimator(_db, count, new TriageEstimator(), NullLogger<LearnedTriageEstimator>.Instance);
        for (var i = 0; i < 9; i++) await store.RecordAsync(Sample(Guid.NewGuid()));
        Assert.Equal(0, await store.TrainAsync());
        Assert.Equal("cold-start", (await Estimate(estimator)).EstimatedBy);
        var tenth = Sample(Guid.NewGuid());
        await store.RecordAsync(tenth);
        await store.RecordAsync(tenth);
        Assert.Equal(10, await _db.TriageEstimatorSamples.CountAsync());
        Assert.Equal(1, await store.TrainAsync());
        Assert.Equal(0, await store.TrainAsync());
        count.Value = 9;
        Assert.Equal("cold-start", (await Estimate(estimator)).EstimatedBy);
        count.Value = 10;
        var learned = await Estimate(estimator);
        Assert.StartsWith("learned:", learned.EstimatedBy);
        Assert.InRange(learned.CostP50!.Value, 49.9, 50.1);
        Assert.InRange(learned.TimeP50!.Value, 1.99, 2.01);
        Assert.True(learned.CostP90 >= learned.CostP50);
        Assert.True(learned.TimeP90 >= learned.TimeP50);
        Assert.Equal("cold-start", (await estimator.EstimateAsync("owner-b", TriageTemplateId.BugFix, TriageSeverity.Moderate, "repo", "summary")).EstimatedBy);
        Assert.Equal("cold-start", (await estimator.EstimateAsync("owner-a", TriageTemplateId.Feature, TriageSeverity.Moderate, "repo", "summary")).EstimatedBy);
        await store.RecordAsync(Sample(Guid.NewGuid(), cost: 100));
        Assert.Equal(1, await store.TrainAsync());
        Assert.Equal(2, (await _db.TriageEstimatorModels.SingleAsync()).Version);
        count.Fail = true;
        Assert.Equal("cold-start", (await Estimate(estimator)).EstimatedBy);
    }

    [Theory]
    [InlineData(null, 7200d, false)]
    [InlineData(50d, null, false)]
    [InlineData(50d, 7200d, true)]
    [InlineData(-1d, 7200d, false)]
    public async Task UnknownProjectedOrNegativeActuals_AreNotTrainingEvidence(double? cost, double? seconds, bool projected)
    {
        await new EstimateTrainingStore(_db).RecordAsync(Sample(Guid.NewGuid(), cost, seconds, projected));
        Assert.Empty(_db.TriageEstimatorSamples);
    }

    [Fact]
    public async Task ZeroActuals_AreValid_AndReplayCannotMoveSampleToAnotherTenant()
    {
        var id = Guid.NewGuid();
        var store = new EstimateTrainingStore(_db);
        await store.RecordAsync(Sample(id, 0, 0));
        await store.RecordAsync(Sample(id, 500, 800, tenant: "owner-b"));
        var row = await _db.TriageEstimatorSamples.SingleAsync();
        Assert.Equal("owner-a", row.TenantId);
        Assert.Equal(0, row.ActualCostUsd);
    }

    [Fact]
    public void Features_AreStableAndUseSeverityRepositoryAndDescription()
    {
        var first = EstimatorFeatures.Create("moderate", "Repo", "summary 123");
        Assert.Equal(first, EstimatorFeatures.Create("Moderate", "repo", "summary 123"));
        Assert.NotEqual(first, EstimatorFeatures.Create("critical", "repo", "summary 123"));
        Assert.NotEqual(first, EstimatorFeatures.Create("moderate", "different", "summary 123"));
        Assert.NotEqual(first, EstimatorFeatures.Create("moderate", "repo", "long different description"));
        Assert.All(first, value => Assert.True(double.IsFinite(value)));
    }

    private static Task<Andy.Issues.Domain.ValueTypes.EstimateSlot> Estimate(LearnedTriageEstimator estimator) =>
        estimator.EstimateAsync("owner-a", TriageTemplateId.BugFix, TriageSeverity.Moderate, "repo", "summary");
    private static byte[] Sample(Guid id, double? cost = 50, double? seconds = 7200, bool projected = false, string tenant = "owner-a") =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = 3,
            goal_id = id,
            tenant_id = tenant,
            template_key = "andy-issues:bug",
            severity = "moderate",
            repository = "repo",
            description_summary = "summary",
            at = DateTimeOffset.UtcNow,
            actual = new { cost_usd_p50 = cost, duration_sec_p50 = seconds, includes_projections = projected }
        });
    private sealed class Counts : ICompletionCountClient
    {
        public int? Value { get; set; }
        public bool Fail { get; set; }
        public Task<int?> GetAsync(string tenant, string template, CancellationToken ct) =>
            Fail ? throw new HttpRequestException("Tasks unavailable") : Task.FromResult(Value);
    }
}
