using System.Security.Cryptography;
using System.Text;
using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Infrastructure.Estimation;

/// <summary>Version 1 bounded, deterministic features; no process-random string hash.</summary>
public static class EstimatorFeatures
{
    public const int Count = 12;
    public static string TemplateKey(TriageTemplateId template) => template switch
    {
        TriageTemplateId.BugFix => "andy-issues:bug",
        TriageTemplateId.Feature => "andy-issues:feature",
        TriageTemplateId.IncidentResponse => "andy-issues:incident",
        TriageTemplateId.Upgrade => "andy-issues:upgrade",
        _ => "unknown"
    };

    public static double[] Create(string severity, string? repository, string? description)
    {
        var x = new double[Count];
        x[0] = 1;
        x[1] = severity.Equals("moderate", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        x[2] = severity.Equals("critical", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var repo = (repository ?? "").Trim().ToLowerInvariant();
        var repoHash = SHA256.HashData(Encoding.UTF8.GetBytes(repo));
        x[3 + repoHash[0] % 4] = 1;
        var text = (description ?? "")[..Math.Min(description?.Length ?? 0, 8192)].ToLowerInvariant();
        x[7] = Math.Log(1 + text.Length) / 10;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(word));
            x[8 + hash[0] % 4] += (hash[1] & 1) == 0 ? 1 : -1;
        }
        var scale = Math.Max(1, Math.Sqrt(words.Length));
        for (var i = 8; i < Count; i++) x[i] /= scale;
        return x;
    }
}
