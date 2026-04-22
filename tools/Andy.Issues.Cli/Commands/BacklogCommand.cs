// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.CommandLine;
using System.CommandLine.Invocation;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;

namespace Andy.Issues.Cli.Commands;

public static class BacklogCommand
{
    public static Command Build(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var cmd = new Command("backlog", "Manage backlog (epics, features, stories)");

        cmd.AddCommand(BuildList(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildAddEpic(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildAddFeature(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildAddStory(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildSetStatus(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildSyncAzdo(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildDraft(apiUrlOption, tokenOption));

        return cmd;
    }

    private static Command BuildList(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var repoIdArg = new Argument<string>("repo-id", "Repository ID (GUID)");
        var jsonOption = new Option<bool>("--json", "Output raw JSON");

        var cmd = new Command("list", "Get backlog for a repository") { repoIdArg, jsonOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var repoId = ctx.ParseResult.GetValueForArgument(repoIdArg);
            var json = ctx.ParseResult.GetValueForOption(jsonOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var backlog = await api.GetAsync<BacklogDto>($"api/repositories/{repoId}/backlog");
            if (backlog is null) return;

            if (json) { Console.WriteLine(ApiClient.ToJson(backlog)); return; }

            foreach (var epic in backlog.Epics)
            {
                Console.WriteLine($"{epic.DisplayId}  {epic.Title}");
                foreach (var feature in epic.Features)
                {
                    Console.WriteLine($"  {feature.DisplayId}  {feature.Title}");
                    foreach (var story in feature.Stories)
                    {
                        var pts = story.StoryPoints.HasValue ? $" ({story.StoryPoints}pts)" : "";
                        Console.WriteLine($"    {story.DisplayId}  [{story.Status,-10}] {story.Title}{pts}");
                    }
                }
            }
        });
        return cmd;
    }

    private static Command BuildAddEpic(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var repoIdArg = new Argument<string>("repo-id", "Repository ID (GUID)");
        var titleArg = new Argument<string>("title", "Epic title");
        var descOption = new Option<string?>("--description", "Epic description");

        var cmd = new Command("add-epic", "Create an epic") { repoIdArg, titleArg, descOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var repoId = ctx.ParseResult.GetValueForArgument(repoIdArg);
            var title = ctx.ParseResult.GetValueForArgument(titleArg);
            var desc = ctx.ParseResult.GetValueForOption(descOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var epic = await api.PostAsync<EpicDto>($"api/repositories/{repoId}/epics",
                new CreateEpicRequest(title, desc, null, null));
            if (epic is null) return;

            Console.WriteLine($"Epic created: {epic.DisplayId}  {epic.Title}  [{epic.Id}]");
        });
        return cmd;
    }

    private static Command BuildAddFeature(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var epicIdArg = new Argument<string>("epic-id", "Epic ID (GUID)");
        var titleArg = new Argument<string>("title", "Feature title");
        var descOption = new Option<string?>("--description", "Feature description");

        var cmd = new Command("add-feature", "Create a feature under an epic") { epicIdArg, titleArg, descOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var epicId = ctx.ParseResult.GetValueForArgument(epicIdArg);
            var title = ctx.ParseResult.GetValueForArgument(titleArg);
            var desc = ctx.ParseResult.GetValueForOption(descOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var feature = await api.PostAsync<FeatureDto>($"api/epics/{epicId}/features",
                new CreateFeatureRequest(title, desc, null, null));
            if (feature is null) return;

            Console.WriteLine($"Feature created: {feature.DisplayId}  {feature.Title}  [{feature.Id}]");
        });
        return cmd;
    }

    private static Command BuildAddStory(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var featureIdArg = new Argument<string>("feature-id", "Feature ID (GUID)");
        var titleArg = new Argument<string>("title", "Story title");
        var descOption = new Option<string?>("--description", "Story description");
        var acOption = new Option<string?>("--acceptance-criteria", "Acceptance criteria");
        var ptsOption = new Option<int?>("--points", "Story points estimate");

        var cmd = new Command("add-story", "Create a user story under a feature")
        {
            featureIdArg, titleArg, descOption, acOption, ptsOption
        };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var featureId = ctx.ParseResult.GetValueForArgument(featureIdArg);
            var title = ctx.ParseResult.GetValueForArgument(titleArg);
            var desc = ctx.ParseResult.GetValueForOption(descOption);
            var ac = ctx.ParseResult.GetValueForOption(acOption);
            var pts = ctx.ParseResult.GetValueForOption(ptsOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var story = await api.PostAsync<UserStoryDto>($"api/features/{featureId}/stories",
                new CreateUserStoryRequest(title, desc, ac, pts, null, null));
            if (story is null) return;

            Console.WriteLine($"Story created: {story.DisplayId}  {story.Title}  [{story.Status}]  [{story.Id}]");
        });
        return cmd;
    }

    private static Command BuildSetStatus(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var storyIdArg = new Argument<string>("story-id", "Story ID (GUID)");
        var statusArg = new Argument<string>("status", "New status (Draft, Ready, InProgress, Done)");
        var prUrlOption = new Option<string?>("--pr-url", "Pull request URL (optional, for Done)");

        var cmd = new Command("set-status", "Update a story's status") { storyIdArg, statusArg, prUrlOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var storyId = ctx.ParseResult.GetValueForArgument(storyIdArg);
            var status = ctx.ParseResult.GetValueForArgument(statusArg);
            var prUrl = ctx.ParseResult.GetValueForOption(prUrlOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var story = await api.PatchAsync<UserStoryDto>($"api/stories/{storyId}/status",
                new UpdateUserStoryStatusRequest(status, prUrl));
            if (story is null) return;

            Console.WriteLine($"Story {story.DisplayId} status: {story.Status}");
        });
        return cmd;
    }

    private static Command BuildSyncAzdo(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var repoIdArg = new Argument<string>("repo-id", "Repository ID (GUID)");
        var cmd = new Command("sync-azdo", "Sync backlog to/from Azure DevOps") { repoIdArg };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var repoId = ctx.ParseResult.GetValueForArgument(repoIdArg);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var result = await api.PostAsync<SyncResult>($"api/repositories/{repoId}/sync-azure-devops");
            if (result is null) return;

            Console.WriteLine($"Added: {result.Added}, Updated: {result.Updated}, Skipped: {result.Skipped}");
            foreach (var err in result.Errors)
                Console.Error.WriteLine($"  Error: {err}");
        });
        return cmd;
    }

    private static Command BuildDraft(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var repoIdArg = new Argument<string>("repo-id", "Repository ID (GUID)");
        var jsonOption = new Option<bool>("--json", "Output raw JSON");

        var cmd = new Command("draft", "Generate a draft backlog using AI") { repoIdArg, jsonOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var repoId = ctx.ParseResult.GetValueForArgument(repoIdArg);
            var json = ctx.ParseResult.GetValueForOption(jsonOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var backlog = await api.PostAsync<BacklogDto>($"api/repositories/{repoId}/generate-backlog");
            if (backlog is null) return;

            if (json) { Console.WriteLine(ApiClient.ToJson(backlog)); return; }

            var storyCount = backlog.Epics.Sum(e => e.Features.Sum(f => f.Stories.Count));
            Console.WriteLine($"Draft backlog generated: {backlog.Epics.Count} epics, {storyCount} stories.");
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
