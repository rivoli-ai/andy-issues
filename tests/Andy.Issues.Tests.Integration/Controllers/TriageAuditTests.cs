using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Messaging;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.ValueTypes;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Andy.Issues.Tests.Integration.Controllers;

public class TriageAuditTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient client = factory.CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static TriageOutput Output => new(TriageTemplateId.BugFix, TriageSeverity.Critical, null, "The service fails under load.", [], new(null, null, null, null, null));
    private async Task<Guid> Create()
    {
        var response = await client.PostAsJsonAsync("/api/triage", new { title = "Audit me", body = "Failure" });
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<IssueDto>(JsonOptions))!.Id;
    }
    [Fact]
    public async Task Completion_PinsInputSnapshotAndReadableOutput_AndPublishesAuditContract()
    {
        var issue = await Create();
        var input = new DocsRef(Guid.NewGuid(), Guid.NewGuid());
        (await client.PostAsJsonAsync($"/api/triage/{issue}/attachments", input)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/triage/{issue}/start", null)).EnsureSuccessStatusCode();
        var later = new DocsRef(Guid.NewGuid(), Guid.NewGuid());
        (await client.PostAsJsonAsync($"/api/triage/{issue}/attachments", later)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/triage/{issue}/complete", Output, JsonOptions)).EnsureSuccessStatusCode();
        var detail = await client.GetFromJsonAsync<IssueDto>($"/api/triage/{issue}", JsonOptions);
        Assert.Equal(input, Assert.Single(detail!.TriageInputDocsRefs!));
        Assert.Equal(input, Assert.Single(detail.TriageOutput!.InputsDocsRefs));
        var reference = detail.TriageOutputDocRef!.Value;
        Assert.Contains(Output.Rationale, factory.FakeDocsClient.Content[reference.DocumentId]);
        Assert.Equal(issue, factory.FakeDocsClient.Links[reference.LinkId].IssueId);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Outbox.SingleAsync(x => x.Subject == $"andy.issues.events.issue.{issue}.triaged");
        using var json = JsonDocument.Parse(row.PayloadJson); var payload = json.RootElement;
        Assert.Equal("critical", payload.GetProperty("severity").GetString());
        Assert.Equal(input.DocumentId, payload.GetProperty("input_docs_refs")[0].GetProperty("document_id").GetGuid());
        Assert.Equal(reference.DocumentId, payload.GetProperty("output_doc_ref").GetProperty("document_id").GetGuid());
        Assert.Equal(reference.LinkId, payload.GetProperty("output_doc_ref").GetProperty("link_id").GetGuid());
    }
    [Fact]
    public async Task FailedDocumentWrite_LeavesStateAndOutboxUnchanged_AndCanRetry()
    {
        var issue = await Create(); (await client.PostAsync($"/api/triage/{issue}/start", null)).EnsureSuccessStatusCode();
        factory.FakeDocsClient.FailWrites = true;
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync($"/api/triage/{issue}/complete", Output, JsonOptions)).StatusCode);
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(TriageState.Triaging, (await db.Issues.FindAsync(issue))!.TriageState);
            Assert.False(await db.Outbox.AnyAsync(x => x.Subject == $"andy.issues.events.issue.{issue}.triaged"));
        }
        finally { factory.FakeDocsClient.FailWrites = false; }
        (await client.PostAsJsonAsync($"/api/triage/{issue}/complete", Output, JsonOptions)).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task SuppliedReference_IsReused_AndStaleRunCannotComplete()
    {
        var issue = await Create(); (await client.PostAsync($"/api/triage/{issue}/start", null)).EnsureSuccessStatusCode();
        var runId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Issues.FindAsync(issue))!.TriageRunId = runId; await db.SaveChangesAsync();
        var reference = await factory.FakeDocsClient.PutTriageOutputAsync(issue, runId, "Original agent output");
        var service = scope.ServiceProvider.GetRequiredService<IIssueService>();
        var stale = await service.CompleteTriageAsync(issue, "dev-user", Output, outputDocRef: reference, runId: Guid.NewGuid());
        Assert.Equal(IssueTriageOutcome.InvalidTransition, stale.Outcome);
        var completed = await service.CompleteTriageAsync(issue, "dev-user", Output, outputDocRef: reference, runId: runId);
        Assert.Equal(IssueTriageOutcome.Updated, completed.Outcome);
        Assert.Equal(reference, completed.Issue!.TriageOutputDocRef); Assert.Equal(runId, completed.Issue.RunId);
        var outputCount = factory.FakeDocsClient.Links.Values.Count(x => x.IssueId == issue); Assert.Equal(1, outputCount);
    }
    [Fact]
    public async Task ContainerDelivery_RetriesSameMessageAfterDocumentFailure()
    {
        var issue = await Create(); (await client.PostAsync($"/api/triage/{issue}/start", null)).EnsureSuccessStatusCode();
        var run = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Issues.FindAsync(issue))!.TriageRunId = run; await db.SaveChangesAsync();
        }
        var reference = await factory.FakeDocsClient.PutTriageOutputAsync(issue, run, JsonSerializer.Serialize(Output, EventJson.Options));
        var payload = new Andy.Issues.Application.Messaging.Events.ContainerRunEventPayload(run, null, "finished", 0, 1,
            OutputArtifacts: [new("triage-output.md", "triage-output.md", 100, "hash", "text/markdown", reference)]);
        var message = new TestMessage
        {
            Subject = $"andy.containers.events.run.{run}.finished",
            Headers = MessageHeaders.NewRoot(),
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload, EventJson.Options),
            ReceivedAt = DateTimeOffset.UtcNow
        };
        var consumer = new Andy.Issues.Infrastructure.Messaging.Consumers.ContainerRunEventConsumer(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), factory.Services.GetRequiredService<IMessageBus>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Andy.Issues.Infrastructure.Messaging.Consumers.ContainerRunEventConsumer>.Instance);
        factory.FakeDocsClient.FailWrites = true;
        try { await Assert.ThrowsAsync<HttpRequestException>(() => consumer.HandleAsync(message, CancellationToken.None)); Assert.False(message.Acked); }
        finally { factory.FakeDocsClient.FailWrites = false; }
        await consumer.HandleAsync(message, CancellationToken.None);
        Assert.True(message.Acked);
        var detail = await client.GetFromJsonAsync<IssueDto>($"/api/triage/{issue}", JsonOptions);
        Assert.Equal("Triaged", detail!.TriageState); Assert.Equal(run, detail.RunId);
    }

    private sealed class TestMessage : IncomingMessage
    {
        public bool Acked { get; private set; }
        public override Task AckAsync(CancellationToken ct = default) { Acked = true; return Task.CompletedTask; }
        public override Task NackAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

}
