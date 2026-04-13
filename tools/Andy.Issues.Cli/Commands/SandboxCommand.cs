// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;

namespace Andy.Issues.Cli.Commands;

public static class SandboxCommand
{
    public static Command Build(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var cmd = new Command("sandbox", "Manage sandboxes (container-based dev environments)");

        cmd.AddCommand(BuildCreate(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildList(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildGet(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildConnect(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildDestroy(apiUrlOption, tokenOption));

        return cmd;
    }

    private static Command BuildCreate(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var repoIdArg = new Argument<string>("repo-id", "Repository ID (GUID)");
        var branchOption = new Option<string>("--branch", "Branch name to check out") { IsRequired = true };

        var cmd = new Command("create", "Create a new sandbox") { repoIdArg, branchOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var repoId = ctx.ParseResult.GetValueForArgument(repoIdArg);
            var branch = ctx.ParseResult.GetValueForOption(branchOption)!;

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var sandbox = await api.PostAsync<SandboxDto>("api/sandboxes",
                new CreateSandboxRequest(Guid.Parse(repoId), branch, null));
            if (sandbox is null) return;

            Console.WriteLine($"Sandbox created: {sandbox.Id}");
            Console.WriteLine($"  Container: {sandbox.ContainerId}");
            Console.WriteLine($"  Branch:    {sandbox.Branch}");
            Console.WriteLine($"  Status:    {sandbox.Status}");
        });
        return cmd;
    }

    private static Command BuildList(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var jsonOption = new Option<bool>("--json", "Output raw JSON");
        var cmd = new Command("list", "List your sandboxes") { jsonOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var json = ctx.ParseResult.GetValueForOption(jsonOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var list = await api.GetAsync<List<SandboxDto>>("api/sandboxes");
            if (list is null) return;

            if (json) { Console.WriteLine(ApiClient.ToJson(list)); return; }

            if (list.Count == 0)
            {
                Console.WriteLine("No sandboxes.");
                return;
            }

            foreach (var s in list)
            {
                Console.WriteLine($"  {s.Id}  {s.Status,-10}  branch={s.Branch}  repo={s.RepositoryId}");
            }
        });
        return cmd;
    }

    private static Command BuildGet(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var idArg = new Argument<string>("id", "Sandbox ID (GUID)");
        var jsonOption = new Option<bool>("--json", "Output raw JSON");

        var cmd = new Command("get", "Get sandbox details") { idArg, jsonOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForArgument(idArg);
            var json = ctx.ParseResult.GetValueForOption(jsonOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var sandbox = await api.GetAsync<SandboxDto>($"api/sandboxes/{id}");
            if (sandbox is null) return;

            if (json) { Console.WriteLine(ApiClient.ToJson(sandbox)); return; }

            Console.WriteLine($"ID:         {sandbox.Id}");
            Console.WriteLine($"Container:  {sandbox.ContainerId}");
            Console.WriteLine($"Repository: {sandbox.RepositoryId}");
            Console.WriteLine($"Branch:     {sandbox.Branch}");
            Console.WriteLine($"Status:     {sandbox.Status}");
            if (sandbox.IdeEndpoint is not null) Console.WriteLine($"IDE:        {sandbox.IdeEndpoint}");
            if (sandbox.VncEndpoint is not null) Console.WriteLine($"VNC:        {sandbox.VncEndpoint}");
        });
        return cmd;
    }

    private static Command BuildConnect(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var idArg = new Argument<string>("id", "Sandbox ID (GUID)");
        var openOption = new Option<bool>("--open", "Open the IDE URL in the default browser");

        var cmd = new Command("connect", "Get connection info for a sandbox") { idArg, openOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForArgument(idArg);
            var open = ctx.ParseResult.GetValueForOption(openOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var conn = await api.GetAsync<SandboxConnectionDto>($"api/sandboxes/{id}/connection");
            if (conn is null) return;

            if (conn.IdeEndpoint is not null) Console.WriteLine($"IDE: {conn.IdeEndpoint}");
            if (conn.VncEndpoint is not null) Console.WriteLine($"VNC: {conn.VncEndpoint}");
            if (conn.SshEndpoint is not null) Console.WriteLine($"SSH: {conn.SshEndpoint}");

            if (open && conn.IdeEndpoint is not null)
            {
                try { Process.Start(new ProcessStartInfo(conn.IdeEndpoint) { UseShellExecute = true }); }
                catch { Console.Error.WriteLine("Could not open browser."); }
            }
        });
        return cmd;
    }

    private static Command BuildDestroy(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var idArg = new Argument<string>("id", "Sandbox ID (GUID)");
        var cmd = new Command("destroy", "Destroy a sandbox") { idArg };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForArgument(idArg);
            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            await api.DeleteAsync($"api/sandboxes/{id}");
            Console.WriteLine("Sandbox destroyed.");
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
