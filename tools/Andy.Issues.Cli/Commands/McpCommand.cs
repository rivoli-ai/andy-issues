// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;

namespace Andy.Issues.Cli.Commands;

public static class McpCommand
{
    public static Command Build(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var cmd = new Command("mcp", "Manage MCP server configurations");

        cmd.AddCommand(BuildList(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildAddStdio(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildAddRemote(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildToggle(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildDiscover(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildDelete(apiUrlOption, tokenOption));

        return cmd;
    }

    private static Command BuildList(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var jsonOption = new Option<bool>("--json", "Output raw JSON");
        var cmd = new Command("list", "List MCP server configurations") { jsonOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var json = ctx.ParseResult.GetValueForOption(jsonOption);
            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var list = await api.GetAsync<List<McpServerConfigDto>>("api/mcp");
            if (list is null) return;

            if (json) { Console.WriteLine(ApiClient.ToJson(list)); return; }

            if (list.Count == 0) { Console.WriteLine("No MCP configurations."); return; }

            foreach (var m in list)
            {
                var scope = m.IsShared ? "shared" : "personal";
                var state = m.Enabled ? "enabled" : "disabled";
                Console.WriteLine($"  {m.Id}  {m.Name,-25} {m.Type,-8} {scope,-8} {state}");
            }
        });
        return cmd;
    }

    private static Command BuildAddStdio(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var nameArg = new Argument<string>("name", "Configuration name");
        var commandArg = new Argument<string>("command", "Command to execute");
        var argsOption = new Option<string?>("--args", "Arguments JSON array (e.g. '[\"--port\",\"8080\"]')");
        var envOption = new Option<string?>("--env", "Environment JSON object (e.g. '{\"KEY\":\"val\"}')");
        var descOption = new Option<string?>("--description", "Description");
        var sharedOption = new Option<bool>("--shared", "Make this configuration shared");

        var cmd = new Command("add", "Add a stdio MCP server configuration")
        {
            nameArg, commandArg, argsOption, envOption, descOption, sharedOption
        };
        // Use alias so we have "add" for stdio (most common)
        cmd.AddAlias("add-stdio");
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var name = ctx.ParseResult.GetValueForArgument(nameArg);
            var command = ctx.ParseResult.GetValueForArgument(commandArg);
            var args = ctx.ParseResult.GetValueForOption(argsOption);
            var env = ctx.ParseResult.GetValueForOption(envOption);
            var desc = ctx.ParseResult.GetValueForOption(descOption);
            var shared = ctx.ParseResult.GetValueForOption(sharedOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var config = await api.PostAsync<McpServerConfigDto>("api/mcp",
                new CreateMcpServerConfigRequest(name, desc, "stdio", command, args, env, null, null, shared ? true : null));
            if (config is null) return;

            Console.WriteLine($"MCP config created: {config.Id}  {config.Name}");
        });
        return cmd;
    }

    private static Command BuildAddRemote(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var nameArg = new Argument<string>("name", "Configuration name");
        var urlArg = new Argument<string>("url", "Remote MCP server URL");
        var headersOption = new Option<string?>("--headers", "Headers JSON object");
        var descOption = new Option<string?>("--description", "Description");
        var sharedOption = new Option<bool>("--shared", "Make this configuration shared");

        var cmd = new Command("add-remote", "Add a remote (SSE/streamable-HTTP) MCP server configuration")
        {
            nameArg, urlArg, headersOption, descOption, sharedOption
        };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var name = ctx.ParseResult.GetValueForArgument(nameArg);
            var url = ctx.ParseResult.GetValueForArgument(urlArg);
            var headers = ctx.ParseResult.GetValueForOption(headersOption);
            var desc = ctx.ParseResult.GetValueForOption(descOption);
            var shared = ctx.ParseResult.GetValueForOption(sharedOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var config = await api.PostAsync<McpServerConfigDto>("api/mcp",
                new CreateMcpServerConfigRequest(name, desc, "remote", null, null, null, url, headers, shared ? true : null));
            if (config is null) return;

            Console.WriteLine($"MCP config created: {config.Id}  {config.Name}");
        });
        return cmd;
    }

    private static Command BuildToggle(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var idArg = new Argument<string>("id", "MCP config ID (GUID)");
        var cmd = new Command("toggle", "Enable or disable an MCP configuration") { idArg };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForArgument(idArg);
            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var config = await api.PostAsync<McpServerConfigDto>($"api/mcp/{id}/toggle");
            if (config is null) return;

            Console.WriteLine($"{config.Name}: {(config.Enabled ? "enabled" : "disabled")}");
        });
        return cmd;
    }

    private static Command BuildDiscover(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var idArg = new Argument<string>("id", "MCP config ID (GUID)");
        var cmd = new Command("discover", "Discover tools exposed by an MCP server") { idArg };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForArgument(idArg);
            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var result = await api.PostAsync<JsonElement>($"api/mcp/{id}/tools");

            Console.WriteLine(result.ValueKind == JsonValueKind.Undefined
                ? "No tools discovered."
                : ApiClient.ToJson(result));
        });
        return cmd;
    }

    private static Command BuildDelete(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var idArg = new Argument<string>("id", "MCP config ID (GUID)");
        var cmd = new Command("delete", "Delete an MCP configuration") { idArg };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForArgument(idArg);
            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            await api.DeleteAsync($"api/mcp/{id}");
            Console.WriteLine("MCP configuration deleted.");
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
