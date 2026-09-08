using System.Text.Json;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.ValueTypes;
using Andy.Issues.Infrastructure.Services;
using Xunit;
namespace Andy.Issues.Tests.Unit.Services;

public class TriageOutputDocumentTests
{
    [Fact]
    public void ParsesMarkdownAndRawJson_RejectsIncompleteOrMalformedClassification()
    {
        var output = new TriageOutput(TriageTemplateId.BugFix, TriageSeverity.Moderate, null, "A reason", [], new(null, null, null, null, null));
        var json = JsonSerializer.Serialize(output, EventJson.Options);
        Assert.Equal(output.Rationale, TriageOutputDocument.Parse(json)!.Rationale);
        Assert.Equal(output.Severity, TriageOutputDocument.Parse("# Result\n\n```json\n" + json + "\n```\n")!.Severity);
        Assert.Null(TriageOutputDocument.Parse("{}")); Assert.Null(TriageOutputDocument.Parse("```json\n{bad}\n```"));
        Assert.Null(TriageOutputDocument.Parse(json.Replace("moderate", "unrecognized")));
        Assert.Null(TriageOutputDocument.Parse(new string('x', 1024 * 1024 + 1)));
    }
}
