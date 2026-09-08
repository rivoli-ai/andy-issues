using System.Text.Json;
using System.Text.RegularExpressions;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Domain.ValueTypes;
namespace Andy.Issues.Infrastructure.Services;

public static class TriageOutputDocument
{
    public static TriageOutput? Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown) || markdown.Length > 1024 * 1024) return null;
        var candidates = new List<string> { markdown.Trim() };
        foreach (Match match in Regex.Matches(markdown, @"(?m)^```json\s*\r?\n([\s\S]*?)^```\s*$", RegexOptions.None, TimeSpan.FromMilliseconds(250)))
            candidates.Add(match.Groups[1].Value);
        foreach (var json in candidates)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !new[] { "template_id", "severity", "rationale", "initial_estimate", "inputs_docs_refs" }.All(k => document.RootElement.TryGetProperty(k, out _))) continue;
                var output = JsonSerializer.Deserialize<TriageOutput>(json, EventJson.Options);
                if (output is not null && output.InitialEstimate is not null && output.InputsDocsRefs is not null &&
                    !string.IsNullOrWhiteSpace(output.Rationale) && Enum.IsDefined(output.TemplateId) && Enum.IsDefined(output.Severity)) return output;
            }
            catch (JsonException) { }
        }
        return null;
    }
}
