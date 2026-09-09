using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.ValueTypes;
using Andy.Issues.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Andy.Issues.Infrastructure.Estimation;

public interface ICompletionCountClient
{
    Task<int?> GetAsync(string tenant, string template, CancellationToken ct);
}

public sealed class CompletionCountClient(IHttpClientFactory clients) : ICompletionCountClient
{
    public async Task<int?> GetAsync(string tenant, string template, CancellationToken ct)
    {
        var client = clients.CreateClient("AndyTasksEstimates");
        if (client.BaseAddress is null) return null;
        using var response = await client.GetAsync($"api/tenants/{Uri.EscapeDataString(tenant)}/templates/{Uri.EscapeDataString(template)}/completion-count", ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CountResponse>(ct);
        return result is { Count: >= 0 } ? result.Count : null;
    }
    private sealed record CountResponse([property: JsonPropertyName("completion_count")] int? Count);
}

public sealed class LearnedTriageEstimator(AppDbContext db, ICompletionCountClient completions, TriageEstimator defaults,
    ILogger<LearnedTriageEstimator> logger) : ITriageEstimator
{
    public EstimateSlot Estimate(string tenantId, TriageTemplateId templateId, TriageSeverity severity) => defaults.Estimate(tenantId, templateId, severity);
    public async Task<EstimateSlot> EstimateAsync(string tenantId, TriageTemplateId templateId, TriageSeverity severity,
        string? repository, string? description, CancellationToken ct = default)
    {
        var fallback = Estimate(tenantId, templateId, severity);
        var key = EstimatorFeatures.TemplateKey(templateId);
        var model = await db.TriageEstimatorModels.FindAsync([tenantId, key], ct);
        if (model is null || model.SampleCount < 10) return fallback;
        try
        {
            if (await completions.GetAsync(tenantId, key, ct) is not (>= 10)) return fallback;
            var fit = JsonSerializer.Deserialize<QuantileModel>(model.ModelJson)!;
            var estimate = fit.Predict(EstimatorFeatures.Create(severity.ToString(), repository, description));
            return new EstimateSlot(estimate.Cost50, estimate.Cost90, estimate.Hours50, estimate.Hours90,
                $"learned:ridge-residual-v1:model-{model.Version}", DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Learned estimate unavailable; using triage defaults");
            return fallback;
        }
    }
}
