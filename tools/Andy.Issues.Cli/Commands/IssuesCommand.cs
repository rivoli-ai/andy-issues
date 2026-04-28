// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.CommandLine;
using System.CommandLine.Invocation;
using Andy.Issues.Application.Dtos;

namespace Andy.Issues.Cli.Commands;

// Z10 — `andy-issues-cli issues {list,get,triage}`. Wraps the same REST
// surface the MCP tools (Z9) hit, so scripts and humans share a path.
// `issues triage` calls the `start` endpoint; the actual agent dispatch
// lands in Z2 — until then this is the state transition only.
public static class IssuesCommand
{
    public static Command Build(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var cmd = new Command("issues", "Manage triage issues");

        cmd.AddCommand(BuildList(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildGet(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildTriage(apiUrlOption, tokenOption));

        return cmd;
    }

    private static Command BuildList(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var stateOption = new Option<string?>(
            "--triage-state",
            "Filter by triage state (NeedsTriage, Triaging, Triaged, Accepted, Rejected)");
        var pageOption = new Option<int>(
            "--page",
            getDefaultValue: () => 1,
            description: "Page number (1-based)");
        var pageSizeOption = new Option<int>(
            "--page-size",
            getDefaultValue: () => 50,
            description: "Page size (max 200)");
        var jsonOption = new Option<bool>("--json", "Output raw JSON");

        var cmd = new Command("list", "List your triage issues") { stateOption, pageOption, pageSizeOption, jsonOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var state = ctx.ParseResult.GetValueForOption(stateOption);
            var page = ctx.ParseResult.GetValueForOption(pageOption);
            var pageSize = ctx.ParseResult.GetValueForOption(pageSizeOption);
            var json = ctx.ParseResult.GetValueForOption(jsonOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var qs = $"?page={page}&pageSize={pageSize}";
            if (!string.IsNullOrWhiteSpace(state))
                qs += $"&triageState={Uri.EscapeDataString(state)}";

            var result = await api.GetAsync<PagedResult<IssueDto>>($"api/triage{qs}");
            if (result is null) return;

            if (json) { Console.WriteLine(ApiClient.ToJson(result)); return; }

            Console.WriteLine($"Total: {result.TotalCount}  Page: {result.Page}/{Math.Max(1, (result.TotalCount + result.PageSize - 1) / result.PageSize)}");
            foreach (var issue in result.Items)
            {
                Console.WriteLine($"  [{issue.TriageState,-12}] {issue.Id}  {issue.Title}");
            }
        });
        return cmd;
    }

    private static Command BuildGet(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var idArg = new Argument<string>("id", "Issue ID (GUID)");
        var jsonOption = new Option<bool>("--json", "Output raw JSON");

        var cmd = new Command("get", "Get a triage issue by ID") { idArg, jsonOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForArgument(idArg);
            var json = ctx.ParseResult.GetValueForOption(jsonOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var issue = await api.GetAsync<IssueDto>($"api/triage/{id}");
            if (issue is null) return;

            if (json) { Console.WriteLine(ApiClient.ToJson(issue)); return; }

            Console.WriteLine($"Issue {issue.Id}");
            Console.WriteLine($"  State:    {issue.TriageState}");
            Console.WriteLine($"  Title:    {issue.Title}");
            if (!string.IsNullOrEmpty(issue.Body))
                Console.WriteLine($"  Body:     {issue.Body}");
            if (issue.RepositoryId is { } repoId)
                Console.WriteLine($"  Repo:     {repoId}");
            if (issue.TriagedAt is { } triagedAt)
                Console.WriteLine($"  Triaged:  {triagedAt:u} by {issue.TriagedBy ?? "?"}");
        });
        return cmd;
    }

    private static Command BuildTriage(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var idArg = new Argument<string>("id", "Issue ID (GUID)");
        var jsonOption = new Option<bool>("--json", "Output raw JSON");

        var cmd = new Command("triage",
            "Re-invoke triage on an issue. Allowed from NeedsTriage or Triaged.")
        { idArg, jsonOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForArgument(idArg);
            var json = ctx.ParseResult.GetValueForOption(jsonOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var issue = await api.PostAsync<IssueDto>($"api/triage/{id}/start");
            if (issue is null) return;

            if (json) { Console.WriteLine(ApiClient.ToJson(issue)); return; }

            Console.WriteLine($"Issue {issue.Id} → {issue.TriageState}");
        });
        return cmd;
    }

    private static ApiClient CreateClient(InvocationContext ctx, Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var apiUrl = ctx.ParseResult.GetValueForOption(apiUrlOption)!;
        var token = ctx.ParseResult.GetValueForOption(tokenOption);
        return new ApiClient(apiUrl, token);
    }
}
