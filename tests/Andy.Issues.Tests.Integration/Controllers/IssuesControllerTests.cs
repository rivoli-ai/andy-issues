// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

// #187 — REST round-trip for the unified `GET /api/issues` endpoint.
// Cockpit AF2 (pipeline kanban: state IN needs-triage,triaging,triaged)
// and AF3 (intake pane: state=needs-triage + assignee=none) both
// route through this one handler — the slice tests below pin both.
//
// Seeding goes through the DbContext directly so we can place issues
// in arbitrary terminal states + arbitrary assignees without driving
// the full lifecycle. The TestAuthHandler authenticates every request
// as `dev-user`, which becomes the owner for ownership-scoped
// queries.
public class IssuesControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public IssuesControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<Guid> SeedIssueAsync(
        string title,
        TriageState state = TriageState.NeedsTriage,
        string? assignee = null,
        Guid? repository = null,
        string owner = "dev-user")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var allocator = scope.ServiceProvider.GetRequiredService<
            Andy.Issues.Application.Interfaces.IBacklogSequenceAllocator>();
        var seq = await allocator.AllocateAsync(BacklogEntityType.Issue);

        var issue = new Issue
        {
            Id = Guid.NewGuid(),
            Seq = seq,
            OwnerUserId = owner,
            AssigneeUserId = assignee,
            RepositoryId = repository,
            Title = title,
        };

        switch (state)
        {
            case TriageState.Triaging:
                issue.StartTriage();
                break;
            case TriageState.Triaged:
                issue.StartTriage();
                issue.CompleteTriage(owner, MakeOutput());
                break;
            case TriageState.Accepted:
                issue.StartTriage();
                issue.CompleteTriage(owner, MakeOutput());
                issue.Accept(owner);
                break;
            case TriageState.Rejected:
                issue.StartTriage();
                issue.CompleteTriage(owner, MakeOutput());
                issue.Reject(owner);
                break;
        }

        db.Issues.Add(issue);
        await db.SaveChangesAsync();
        return issue.Id;
    }

    private static Andy.Issues.Domain.ValueTypes.TriageOutput MakeOutput() =>
        new(
            TriageTemplateId.BugFix,
            TriageSeverity.Critical,
            null,
            "rationale",
            Array.Empty<Andy.Issues.Domain.ValueTypes.DocsRef>(),
            new Andy.Issues.Domain.ValueTypes.EstimateSlot());

    [Fact]
    public async Task List_NoFilters_ReturnsAllOwnerIssues()
    {
        var marker = $"187-no-filter-{Guid.NewGuid()}";
        await SeedIssueAsync($"{marker}-a");
        await SeedIssueAsync($"{marker}-b");

        var resp = await _client.GetAsync("/api/issues");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IssueListResponse>(JsonOptions);

        Assert.NotNull(body);
        Assert.Contains(body!.Items, i => i.Title == $"{marker}-a");
        Assert.Contains(body.Items, i => i.Title == $"{marker}-b");
    }

    [Fact]
    public async Task List_StateCsv_MatchesAnyOf_Af2PipelineSlice()
    {
        // AF2 (rivoli-ai/conductor#684): the leftmost "triage" column
        // of the pipeline kanban — state IN (needs-triage, triaging,
        // triaged).
        var marker = $"187-af2-{Guid.NewGuid()}";
        await SeedIssueAsync($"{marker}-needs", TriageState.NeedsTriage);
        await SeedIssueAsync($"{marker}-triaging", TriageState.Triaging);
        await SeedIssueAsync($"{marker}-triaged", TriageState.Triaged);
        await SeedIssueAsync($"{marker}-accepted", TriageState.Accepted);
        await SeedIssueAsync($"{marker}-rejected", TriageState.Rejected);

        var resp = await _client.GetAsync(
            "/api/issues?state=needs-triage,triaging,triaged&limit=200");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IssueListResponse>(JsonOptions);

        var titles = body!.Items.Where(i => i.Title.StartsWith(marker))
            .Select(i => i.Title).ToList();
        Assert.Contains($"{marker}-needs", titles);
        Assert.Contains($"{marker}-triaging", titles);
        Assert.Contains($"{marker}-triaged", titles);
        Assert.DoesNotContain($"{marker}-accepted", titles);
        Assert.DoesNotContain($"{marker}-rejected", titles);
    }

    [Fact]
    public async Task List_AssigneeNoneStateNeedsTriage_Af3IntakeSlice()
    {
        // AF3 (rivoli-ai/conductor#685): intake pane — state=needs-triage
        // + assignee=none → the drag-to-triage queue.
        var marker = $"187-af3-{Guid.NewGuid()}";
        await SeedIssueAsync($"{marker}-unassigned-needs", TriageState.NeedsTriage, assignee: null);
        await SeedIssueAsync($"{marker}-assigned-needs", TriageState.NeedsTriage, assignee: "bob");
        await SeedIssueAsync($"{marker}-unassigned-triaging", TriageState.Triaging, assignee: null);

        var resp = await _client.GetAsync(
            "/api/issues?state=needs-triage&assignee=none&limit=200");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IssueListResponse>(JsonOptions);

        var titles = body!.Items.Where(i => i.Title.StartsWith(marker))
            .Select(i => i.Title).ToList();
        Assert.Contains($"{marker}-unassigned-needs", titles);
        Assert.DoesNotContain($"{marker}-assigned-needs", titles);
        Assert.DoesNotContain($"{marker}-unassigned-triaging", titles);
    }

    [Fact]
    public async Task List_AssigneeMe_ResolvesToAuthenticatedPrincipal()
    {
        var marker = $"187-me-{Guid.NewGuid()}";
        await SeedIssueAsync($"{marker}-to-dev", assignee: "dev-user");
        await SeedIssueAsync($"{marker}-to-bob", assignee: "bob");

        var resp = await _client.GetAsync("/api/issues?assignee=me&limit=200");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IssueListResponse>(JsonOptions);

        var titles = body!.Items.Where(i => i.Title.StartsWith(marker))
            .Select(i => i.Title).ToList();
        Assert.Contains($"{marker}-to-dev", titles);
        Assert.DoesNotContain($"{marker}-to-bob", titles);
    }

    [Fact]
    public async Task List_RepositoryFilter_ScopesToRepo()
    {
        var marker = $"187-repo-{Guid.NewGuid()}";
        var repoA = Guid.NewGuid();
        var repoB = Guid.NewGuid();
        await SeedIssueAsync($"{marker}-in-a", repository: repoA);
        await SeedIssueAsync($"{marker}-in-b", repository: repoB);

        var resp = await _client.GetAsync($"/api/issues?repository={repoA}&limit=200");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IssueListResponse>(JsonOptions);

        var titles = body!.Items.Where(i => i.Title.StartsWith(marker))
            .Select(i => i.Title).ToList();
        Assert.Contains($"{marker}-in-a", titles);
        Assert.DoesNotContain($"{marker}-in-b", titles);
    }

    [Fact]
    public async Task List_UnknownState_400s_WithError()
    {
        var resp = await _client.GetAsync("/api/issues?state=burning");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var error = await resp.Content.ReadFromJsonAsync<IssueListErrorContract>(JsonOptions);
        Assert.NotNull(error);
        Assert.Contains("burning", error!.Error);
    }

    [Fact]
    public async Task List_CursorPaginates_AndTerminates()
    {
        // Seed five issues with a unique marker so other tests' rows
        // don't muddy the count. Limit=2 → page1+page2+page3 (final).
        var marker = $"187-page-{Guid.NewGuid()}";
        for (var i = 0; i < 5; i++)
        {
            await SeedIssueAsync($"{marker}-{i}");
        }

        var seen = new HashSet<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = "/api/issues?limit=2"
                + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var resp = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<IssueListResponse>(JsonOptions);
            Assert.NotNull(body);

            // Only count rows that belong to this test marker — other
            // tests' seeds may appear when the integration suite runs
            // in parallel against the shared in-memory db.
            foreach (var item in body!.Items.Where(i => i.Title.StartsWith(marker)))
            {
                Assert.True(seen.Add(item.Id), $"cursor returned id twice: {item.Id}");
            }

            cursor = body.Cursor;
            pages++;
            Assert.True(pages < 50, "cursor pagination did not terminate");
        }
        while (cursor is not null);

        Assert.Equal(5, seen.Count);
    }

    // Inline response shape — IssueListErrorResponse from the API
    // assembly isn't re-exported. Keeping a local DTO avoids a
    // ProjectReference for this single record.
    private record IssueListErrorContract(string Error);
}
