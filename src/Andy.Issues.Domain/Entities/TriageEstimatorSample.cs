namespace Andy.Issues.Domain.Entities;

public sealed class TriageEstimatorSample
{
    public Guid GoalId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TemplateKey { get; set; } = string.Empty;
    public string FeaturesJson { get; set; } = "[]";
    public double ActualCostUsd { get; set; }
    public double ActualDurationHours { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class TriageEstimatorModel
{
    public string TenantId { get; set; } = string.Empty;
    public string TemplateKey { get; set; } = string.Empty;
    public int Version { get; set; }
    public int SampleCount { get; set; }
    public string SampleFingerprint { get; set; } = string.Empty;
    public string ModelJson { get; set; } = "{}";
    public DateTimeOffset TrainedAt { get; set; }
}
