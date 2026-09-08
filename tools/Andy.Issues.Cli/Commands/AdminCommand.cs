// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.CommandLine;
using System.CommandLine.Invocation;
using Andy.Issues.Application.Dtos;

namespace Andy.Issues.Cli.Commands;

public static class AdminCommand
{
    public static Command Build(Option<string> apiUrl, Option<string?> token)
    {
        var root = new Command("admin", "Administrative read-only views");
        var users = new Command("users", "Users with Andy Issues roles");
        var query = new Option<string?>("--query", "Search email or display name");
        var role = new Option<string?>("--role", "Filter by application role");
        var skip = new Option<int>("--skip", () => 0, "Rows to skip");
        var take = new Option<int>("--take", () => 50, "Maximum rows (1-200)");
        var list = new Command("list", "List users from Andy RBAC (requires admin permission)") { query, role, skip, take };
        list.SetHandler(async (InvocationContext ctx) =>
        {
            using var api = new ApiClient(ctx.ParseResult.GetValueForOption(apiUrl)!, ctx.ParseResult.GetValueForOption(token));
            var path = $"api/admin/users?query={Uri.EscapeDataString(ctx.ParseResult.GetValueForOption(query) ?? "")}&role={Uri.EscapeDataString(ctx.ParseResult.GetValueForOption(role) ?? "")}&skip={ctx.ParseResult.GetValueForOption(skip)}&take={ctx.ParseResult.GetValueForOption(take)}";
            Console.WriteLine(ApiClient.ToJson(await api.GetAsync<AdminUsersDto>(path)));
        });
        users.AddCommand(list);
        root.AddCommand(users);
        return root;
    }
}
