// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.CommandLine;
using System.CommandLine.Invocation;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;

namespace Andy.Issues.Cli.Commands;

public static class ArtifactFeedsCommand
{
    public static Command Build(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var cmd = new Command("artifact-feeds", "Manage artifact feeds (NuGet/npm registries)");

        cmd.AddCommand(BuildListEnabled(apiUrlOption, tokenOption));

        var adminCmd = new Command("admin", "Admin-only feed management");
        adminCmd.AddCommand(BuildAdminList(apiUrlOption, tokenOption));
        adminCmd.AddCommand(BuildAdminAdd(apiUrlOption, tokenOption));
        adminCmd.AddCommand(BuildAdminDelete(apiUrlOption, tokenOption));
        cmd.AddCommand(adminCmd);

        return cmd;
    }

    private static Command BuildListEnabled(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var jsonOption = new Option<bool>("--json", "Output raw JSON");
        var cmd = new Command("list-enabled", "List enabled artifact feeds") { jsonOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var json = ctx.ParseResult.GetValueForOption(jsonOption);
            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var list = await api.GetAsync<List<ArtifactFeedConfigDto>>("api/artifact/enabled");
            if (list is null) return;

            if (json) { Console.WriteLine(ApiClient.ToJson(list)); return; }

            if (list.Count == 0) { Console.WriteLine("No enabled artifact feeds."); return; }

            foreach (var f in list)
            {
                Console.WriteLine($"  {f.Id}  {f.Name,-25} {f.Type,-6} {f.Organization}/{f.FeedName}");
            }
        });
        return cmd;
    }

    private static Command BuildAdminList(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var jsonOption = new Option<bool>("--json", "Output raw JSON");
        var cmd = new Command("list", "List all artifact feeds (admin)") { jsonOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var json = ctx.ParseResult.GetValueForOption(jsonOption);
            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var list = await api.GetAsync<List<ArtifactFeedConfigDto>>("api/artifact");
            if (list is null) return;

            if (json) { Console.WriteLine(ApiClient.ToJson(list)); return; }

            if (list.Count == 0) { Console.WriteLine("No artifact feeds."); return; }

            foreach (var f in list)
            {
                var state = f.Enabled ? "enabled" : "disabled";
                Console.WriteLine($"  {f.Id}  {f.Name,-25} {f.Type,-6} {state}  {f.Organization}/{f.FeedName}");
            }
        });
        return cmd;
    }

    private static Command BuildAdminAdd(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var nameArg = new Argument<string>("name", "Feed display name");
        var orgArg = new Argument<string>("organization", "Azure DevOps organization");
        var feedArg = new Argument<string>("feed-name", "Azure DevOps feed name");
        var typeOption = new Option<string>("--type", () => "nuget", "Feed type (nuget or npm)");
        var projectOption = new Option<string?>("--project", "Azure DevOps project (if project-scoped)");

        var cmd = new Command("add", "Add an artifact feed (admin)")
        {
            nameArg, orgArg, feedArg, typeOption, projectOption
        };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var name = ctx.ParseResult.GetValueForArgument(nameArg);
            var org = ctx.ParseResult.GetValueForArgument(orgArg);
            var feed = ctx.ParseResult.GetValueForArgument(feedArg);
            var type = ctx.ParseResult.GetValueForOption(typeOption)!;
            var project = ctx.ParseResult.GetValueForOption(projectOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var result = await api.PostAsync<ArtifactFeedConfigDto>("api/artifact",
                new CreateArtifactFeedConfigRequest(name, org, feed, project, type));
            if (result is null) return;

            Console.WriteLine($"Feed created: {result.Id}  {result.Name}");
        });
        return cmd;
    }

    private static Command BuildAdminDelete(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var idArg = new Argument<string>("id", "Feed ID (GUID)");
        var cmd = new Command("delete", "Delete an artifact feed (admin)") { idArg };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForArgument(idArg);
            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            await api.DeleteAsync($"api/artifact/{id}");
            Console.WriteLine("Artifact feed deleted.");
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
