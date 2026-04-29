// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

// Z1 — REST round-trip per state-machine transition. The TestAuthHandler
// authenticates every request as `dev-user`, so issue ownership is
// satisfied by default. Each terminal transition asserts that an outbox
// row was appended in the same unit of work.
public class TriageControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // The API serialises enums as strings (`Program.cs` adds
        // JsonStringEnumConverter to the controller pipeline). Tests
        // need the same converter to read TriageOutput's enum members
        // back from a response body.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TriageControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<List<string>> GetOutboxSubjectsAsync(Guid issueId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Outbox
            .Where(o => o.CorrelationId == issueId)
            .OrderBy(o => o.CreatedAt)
            .Select(o => o.Subject)
            .ToListAsync();
    }

    private async Task<IssueDto> CreateIssueAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/triage",
            new CreateIssueRequest("intake", "body", null));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        return dto!;
    }

    [Fact]
    public async Task Complete_WithTriageOutput_PersistsAndEmitsInPayload()
    {
        var issue = await CreateIssueAsync();
        await _client.PostAsync($"/api/triage/{issue.Id}/start", null);

        // REST uses the API's default camelCase JSON convention. The
        // snake_case schema in schemas/triage-output.v1.json applies to
        // the NATS event payload (EventJson.Options) — Z2's run-finish
        // handler will write output via the consumer path, not REST.
        var output = new
        {
            templateId = "BugFix",
            severity = "Critical",
            suggestedRepo = "rivoli-ai/andy-tasks",
            rationale = "Repro on every save.",
            inputsDocsRefs = Array.Empty<object>(),
            initialEstimate = new { }
        };

        var completeResp = await _client.PostAsJsonAsync(
            $"/api/triage/{issue.Id}/complete", output);
        Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);
        var afterComplete = await completeResp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.NotNull(afterComplete!.TriageOutput);
        Assert.Equal("rivoli-ai/andy-tasks", afterComplete.TriageOutput!.SuggestedRepo);

        // Outbox row carries the same output payload.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.Outbox
            .Where(o => o.CorrelationId == issue.Id && o.Subject.EndsWith(".triaged"))
            .SingleAsync();

        using var doc = JsonDocument.Parse(entry.PayloadJson);
        var triageOutputElem = doc.RootElement.GetProperty("triage_output");
        Assert.Equal("bug_fix", triageOutputElem.GetProperty("template_id").GetString());
        Assert.Equal("critical", triageOutputElem.GetProperty("severity").GetString());
        Assert.Equal("Repro on every save.", triageOutputElem.GetProperty("rationale").GetString());
    }

    [Fact]
    public async Task Complete_WithEmptyRationale_Returns409()
    {
        var issue = await CreateIssueAsync();
        await _client.PostAsync($"/api/triage/{issue.Id}/start", null);

        var bad = new
        {
            templateId = "Feature",
            severity = "Info",
            suggestedRepo = (string?)null,
            rationale = "   ",
            inputsDocsRefs = Array.Empty<object>(),
            initialEstimate = new { }
        };

        var resp = await _client.PostAsJsonAsync($"/api/triage/{issue.Id}/complete", bad);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_AcceptPath_EmitsTriagedAndAccepted()
    {
        var issue = await CreateIssueAsync();
        Assert.Equal("NeedsTriage", issue.TriageState);

        var startResp = await _client.PostAsync($"/api/triage/{issue.Id}/start", null);
        Assert.Equal(HttpStatusCode.OK, startResp.StatusCode);
        var afterStart = await startResp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.Equal("Triaging", afterStart!.TriageState);

        var completeResp = await _client.PostAsync($"/api/triage/{issue.Id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);
        var afterComplete = await completeResp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.Equal("Triaged", afterComplete!.TriageState);
        Assert.NotNull(afterComplete.TriagedAt);

        var acceptResp = await _client.PostAsync($"/api/triage/{issue.Id}/accept", null);
        Assert.Equal(HttpStatusCode.OK, acceptResp.StatusCode);
        var afterAccept = await acceptResp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.Equal("Accepted", afterAccept!.TriageState);

        var subjects = await GetOutboxSubjectsAsync(issue.Id);
        Assert.Equal(2, subjects.Count);
        Assert.EndsWith(".triaged", subjects[0]);
        Assert.EndsWith(".accepted", subjects[1]);
    }

    [Fact]
    public async Task FullLifecycle_RejectPath_EmitsTriagedAndRejected()
    {
        var issue = await CreateIssueAsync();
        await _client.PostAsync($"/api/triage/{issue.Id}/start", null);
        await _client.PostAsync($"/api/triage/{issue.Id}/complete", null);
        var rejectResp = await _client.PostAsync($"/api/triage/{issue.Id}/reject", null);
        Assert.Equal(HttpStatusCode.OK, rejectResp.StatusCode);
        var afterReject = await rejectResp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.Equal("Rejected", afterReject!.TriageState);

        var subjects = await GetOutboxSubjectsAsync(issue.Id);
        Assert.EndsWith(".triaged", subjects[0]);
        Assert.EndsWith(".rejected", subjects[1]);
    }

    [Fact]
    public async Task InvalidTransition_Returns409()
    {
        var issue = await CreateIssueAsync();
        // Cannot accept directly from NeedsTriage.
        var resp = await _client.PostAsync($"/api/triage/{issue.Id}/accept", null);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task UnknownIssue_Returns404()
    {
        var resp = await _client.PostAsync($"/api/triage/{Guid.NewGuid()}/start", null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsIssue()
    {
        var issue = await CreateIssueAsync();
        var resp = await _client.GetAsync($"/api/triage/{issue.Id}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.Equal(issue.Id, dto!.Id);
    }

    [Fact]
    public async Task ReInvokeTriage_FromTriaged_TransitionsBackToTriaging()
    {
        var issue = await CreateIssueAsync();
        await _client.PostAsync($"/api/triage/{issue.Id}/start", null);
        await _client.PostAsync($"/api/triage/{issue.Id}/complete", null);

        // Re-invoke via Z9/Z10 path goes through the same `start` endpoint.
        var reStart = await _client.PostAsync($"/api/triage/{issue.Id}/start", null);
        Assert.Equal(HttpStatusCode.OK, reStart.StatusCode);
        var dto = await reStart.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.Equal("Triaging", dto!.TriageState);
    }

    // ── Z5 — Revisions ──────────────────────────────────────────────

    private static object MakeOutputBody(string rationale = "Repro confirmed.") => new
    {
        templateId = "BugFix",
        severity = "Critical",
        suggestedRepo = "rivoli-ai/andy-tasks",
        rationale,
        inputsDocsRefs = Array.Empty<object>(),
        initialEstimate = new { }
    };

    private async Task<Guid> CreateAndCompleteAsync(string rationale = "First.")
    {
        var issue = await CreateIssueAsync();
        await _client.PostAsync($"/api/triage/{issue.Id}/start", null);
        var resp = await _client.PostAsJsonAsync(
            $"/api/triage/{issue.Id}/complete", MakeOutputBody(rationale));
        resp.EnsureSuccessStatusCode();
        return issue.Id;
    }

    [Fact]
    public async Task PatchOutput_FromTriaged_AppendsRevisionAndEmitsRevised()
    {
        var id = await CreateAndCompleteAsync();

        var resp = await _client.PatchAsJsonAsync(
            $"/api/triage/{id}/output",
            new
            {
                output = MakeOutputBody("Reclassified."),
                diffSummary = "Severity bumped."
            });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.Equal("Reclassified.", dto!.TriageOutput!.Rationale);

        var subjects = await GetOutboxSubjectsAsync(id);
        Assert.Contains(subjects, s => s.EndsWith(".triaged"));
        Assert.Contains(subjects, s => s.EndsWith(".revised"));
    }

    [Fact]
    public async Task PatchOutput_FromNeedsTriage_Returns409()
    {
        var issue = await CreateIssueAsync();
        var resp = await _client.PatchAsJsonAsync(
            $"/api/triage/{issue.Id}/output",
            new { output = MakeOutputBody(), diffSummary = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task GetRevisions_ReturnsNewestFirst()
    {
        var id = await CreateAndCompleteAsync(rationale: "v1");

        // Edit twice so we have three revisions total.
        await _client.PatchAsJsonAsync($"/api/triage/{id}/output",
            new { output = MakeOutputBody("v2"), diffSummary = (string?)null });
        await _client.PatchAsJsonAsync($"/api/triage/{id}/output",
            new { output = MakeOutputBody("v3"), diffSummary = (string?)null });

        var resp = await _client.GetAsync($"/api/triage/{id}/revisions");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var rows = await resp.Content.ReadFromJsonAsync<List<TriageOutputRevisionDto>>(JsonOptions);
        Assert.Equal(3, rows!.Count);
        Assert.Equal("v3", rows[0].TriageOutput.Rationale);
        Assert.Equal("v2", rows[1].TriageOutput.Rationale);
        Assert.Equal("v1", rows[2].TriageOutput.Rationale);
    }

    [Fact]
    public async Task PostRevert_RestoresPriorRevisionAsNewRevision()
    {
        var id = await CreateAndCompleteAsync(rationale: "first");
        await _client.PatchAsJsonAsync($"/api/triage/{id}/output",
            new { output = MakeOutputBody("second"), diffSummary = (string?)null });

        var revsResp = await _client.GetAsync($"/api/triage/{id}/revisions");
        var revs = await revsResp.Content.ReadFromJsonAsync<List<TriageOutputRevisionDto>>(JsonOptions);
        var firstRevisionId = revs!.Single(r => r.TriageOutput.Rationale == "first").Id;

        var revertResp = await _client.PostAsJsonAsync(
            $"/api/triage/{id}/revert", new { targetRevisionId = firstRevisionId });
        Assert.Equal(HttpStatusCode.OK, revertResp.StatusCode);
        var dto = await revertResp.Content.ReadFromJsonAsync<IssueDto>(JsonOptions);
        Assert.Equal("first", dto!.TriageOutput!.Rationale);

        // Final revision count: agent-first, human-second, human-revert = 3.
        var revs2 = await (await _client.GetAsync($"/api/triage/{id}/revisions"))
            .Content.ReadFromJsonAsync<List<TriageOutputRevisionDto>>(JsonOptions);
        Assert.Equal(3, revs2!.Count);
        Assert.StartsWith("Reverted to revision", revs2[0].DiffSummary);
    }

    [Fact]
    public async Task PostRevert_UnknownRevision_Returns404()
    {
        var id = await CreateAndCompleteAsync();
        var resp = await _client.PostAsJsonAsync(
            $"/api/triage/{id}/revert", new { targetRevisionId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── Z8 — Attachments ────────────────────────────────────────────

    [Fact]
    public async Task PostAttachment_HappyPath_Returns201AndAppearsInList()
    {
        var issue = await CreateIssueAsync();
        var docId = Guid.NewGuid();
        var linkId = Guid.NewGuid();

        var attachResp = await _client.PostAsJsonAsync(
            $"/api/triage/{issue.Id}/attachments",
            new { documentId = docId, linkId });
        Assert.Equal(HttpStatusCode.Created, attachResp.StatusCode);
        var dto = await attachResp.Content.ReadFromJsonAsync<IssueAttachmentDto>(JsonOptions);
        Assert.Equal(docId, dto!.DocumentId);
        Assert.Equal(linkId, dto.LinkId);

        var listResp = await _client.GetAsync($"/api/triage/{issue.Id}/attachments");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var list = await listResp.Content.ReadFromJsonAsync<List<IssueAttachmentDto>>(JsonOptions);
        Assert.Single(list!);
    }

    [Fact]
    public async Task PostAttachment_DuplicateLink_Returns200_NotDuplicated()
    {
        var issue = await CreateIssueAsync();
        var body = new { documentId = Guid.NewGuid(), linkId = Guid.NewGuid() };

        var first = await _client.PostAsJsonAsync($"/api/triage/{issue.Id}/attachments", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync($"/api/triage/{issue.Id}/attachments", body);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var list = await (await _client.GetAsync($"/api/triage/{issue.Id}/attachments"))
            .Content.ReadFromJsonAsync<List<IssueAttachmentDto>>(JsonOptions);
        Assert.Single(list!);
    }

    [Fact]
    public async Task PostAttachment_AfterAccept_Returns409()
    {
        var id = await CreateAndCompleteAsync();
        await _client.PostAsync($"/api/triage/{id}/accept", null);

        var resp = await _client.PostAsJsonAsync(
            $"/api/triage/{id}/attachments",
            new { documentId = Guid.NewGuid(), linkId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task PostAttachment_EmptyLinkId_Returns400()
    {
        var issue = await CreateIssueAsync();
        var resp = await _client.PostAsJsonAsync(
            $"/api/triage/{issue.Id}/attachments",
            new { documentId = Guid.NewGuid(), linkId = Guid.Empty });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteAttachment_RemovesRowAnd204()
    {
        var issue = await CreateIssueAsync();
        var linkId = Guid.NewGuid();
        await _client.PostAsJsonAsync($"/api/triage/{issue.Id}/attachments",
            new { documentId = Guid.NewGuid(), linkId });

        var deleteResp = await _client.DeleteAsync($"/api/triage/{issue.Id}/attachments/{linkId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var list = await (await _client.GetAsync($"/api/triage/{issue.Id}/attachments"))
            .Content.ReadFromJsonAsync<List<IssueAttachmentDto>>(JsonOptions);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task DeleteAttachment_UnknownLink_Returns404()
    {
        var issue = await CreateIssueAsync();
        var resp = await _client.DeleteAsync($"/api/triage/{issue.Id}/attachments/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
