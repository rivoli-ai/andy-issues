using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Infrastructure.Estimation;

public sealed class EstimateTrainingStore(AppDbContext db)
{
    public async Task RecordAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        TriageEstimatorSample? sample;
        try { sample = ReadSample(payload); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or KeyNotFoundException)
        { throw new JsonException("Malformed estimate training sample.", ex); }
        if (sample is null || await db.TriageEstimatorSamples.AnyAsync(s => s.GoalId == sample.GoalId, ct)) return;
        db.TriageEstimatorSamples.Add(sample);
        await db.SaveChangesAsync(ct);
    }

    private static TriageEstimatorSample? ReadSample(ReadOnlyMemory<byte> payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (!root.TryGetProperty("schema_version", out var version) || version.GetInt32() != 3) return null;
        var goalId = root.GetProperty("goal_id").GetGuid();
        var tenant = root.GetProperty("tenant_id").GetString();
        var template = root.GetProperty("template_key").GetString();
        var severity = root.GetProperty("severity").GetString();
        if (goalId == Guid.Empty || string.IsNullOrWhiteSpace(tenant) || tenant.Length > 256
            || template is not ("andy-issues:bug" or "andy-issues:feature" or "andy-issues:incident" or "andy-issues:upgrade")
            || severity is not ("info" or "moderate" or "critical")) return null;
        if (!root.TryGetProperty("actual", out var actual)) return null;
        if (actual.ValueKind != JsonValueKind.Object
            || (actual.TryGetProperty("includes_projections", out var projected) && projected.GetBoolean())
            || !actual.TryGetProperty("cost_usd_p50", out var costJson) || costJson.ValueKind != JsonValueKind.Number || !costJson.TryGetDouble(out var cost)
            || !actual.TryGetProperty("duration_sec_p50", out var secondsJson) || secondsJson.ValueKind != JsonValueKind.Number || !secondsJson.TryGetDouble(out var seconds)
            || !double.IsFinite(cost) || cost < 0 || cost > 1e9 || !double.IsFinite(seconds) || seconds < 0 || seconds > 1e10) return null;
        var repository = root.TryGetProperty("repository", out var repo) ? repo.GetString() : null;
        var description = root.TryGetProperty("description_summary", out var desc) ? desc.GetString() : null;
        return new TriageEstimatorSample
        {
            GoalId = goalId,
            TenantId = tenant,
            TemplateKey = template,
            FeaturesJson = JsonSerializer.Serialize(EstimatorFeatures.Create(severity, repository, description)),
            ActualCostUsd = cost,
            ActualDurationHours = seconds / 3600,
            RecordedAt = root.GetProperty("at").GetDateTimeOffset().ToUniversalTime(),
        };
    }

    public async Task<int> TrainAsync(CancellationToken ct = default)
    {
        var buckets = await db.TriageEstimatorSamples.Select(s => new { s.TenantId, s.TemplateKey }).Distinct().ToListAsync(ct);
        var trained = 0;
        foreach (var bucket in buckets)
        {
            // Bound CPU and retained training window independently for each tenant.
            var samples = await db.TriageEstimatorSamples.AsNoTracking()
                .Where(s => s.TenantId == bucket.TenantId && s.TemplateKey == bucket.TemplateKey)
                .OrderByDescending(s => s.RecordedAt).ThenBy(s => s.GoalId).Take(1000).ToListAsync(ct);
            if (samples.Count < 10) continue;
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Join(",", samples.Select(s => s.GoalId)))));
            var model = await db.TriageEstimatorModels.FindAsync([bucket.TenantId, bucket.TemplateKey], ct);
            if (model?.SampleFingerprint == fingerprint) continue;
            var fit = QuantileModel.Train(samples.Select(s => (
                JsonSerializer.Deserialize<double[]>(s.FeaturesJson)!, s.ActualCostUsd, s.ActualDurationHours)).ToArray());
            if (model is null)
            {
                model = new TriageEstimatorModel { TenantId = bucket.TenantId, TemplateKey = bucket.TemplateKey };
                db.TriageEstimatorModels.Add(model);
            }
            model.Version++;
            model.SampleCount = samples.Count;
            model.SampleFingerprint = fingerprint;
            model.ModelJson = JsonSerializer.Serialize(fit);
            model.TrainedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            trained++;
        }
        return trained;
    }
}
