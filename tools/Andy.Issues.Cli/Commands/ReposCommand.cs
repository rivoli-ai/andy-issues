// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.CommandLine;
using System.CommandLine.Invocation;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Requests;

namespace Andy.Issues.Cli.Commands;

public static class ReposCommand
{
    public static Command Build(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var cmd = new Command("repos", "Manage repositories");

        cmd.AddCommand(BuildList(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildGet(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildDelete(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildSyncGitHub(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildSyncAzdo(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildShare(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildSetLlm(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildSetAzureIdentity(apiUrlOption, tokenOption));
        cmd.AddCommand(BuildVerifyAzureIdentity(apiUrlOption, tokenOption));

        return cmd;
    }

    private static Command BuildList(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var scopeOption = new Option<string>("--scope", () => "mine", "Filter scope: mine, shared, or all");
        var jsonOption = new Option<bool>("--json", "Output raw JSON");
        var pageOption = new Option<int>("--page", () => 1, "Page number");
        var pageSizeOption = new Option<int>("--page-size", () => 20, "Page size");

        var cmd = new Command("list", "List repositories") { scopeOption, jsonOption, pageOption, pageSizeOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var scope = ctx.ParseResult.GetValueForOption(scopeOption)!;
            var json = ctx.ParseResult.GetValueForOption(jsonOption);
            var page = ctx.ParseResult.GetValueForOption(pageOption);
            var pageSize = ctx.ParseResult.GetValueForOption(pageSizeOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var result = await api.GetAsync<PagedResult<RepositoryDto>>(
                $"api/repositories?scope={scope}&page={page}&pageSize={pageSize}");
            if (result is null) return;

            if (json)
            {
                Console.WriteLine(ApiClient.ToJson(result));
                return;
            }

            Console.WriteLine($"Repositories (page {result.Page}/{(result.TotalCount + result.PageSize - 1) / Math.Max(result.PageSize, 1)}, total {result.TotalCount}):");
            Console.WriteLine();
            foreach (var r in result.Items)
            {
                Console.WriteLine($"  {r.Id}  {r.Name,-30} {r.Provider,-12} {r.CloneUrl}");
            }
        });
        return cmd;
    }

    private static Command BuildGet(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var idArg = new Argument<string>("id", "Repository ID (GUID)");
        var jsonOption = new Option<bool>("--json", "Output raw JSON");

        var cmd = new Command("get", "Get repository details") { idArg, jsonOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForArgument(idArg);
            var json = ctx.ParseResult.GetValueForOption(jsonOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var repo = await api.GetAsync<RepositoryDto>($"api/repositories/{id}");
            if (repo is null) return;

            if (json) { Console.WriteLine(ApiClient.ToJson(repo)); return; }

            Console.WriteLine($"Name:            {repo.Name}");
            Console.WriteLine($"ID:              {repo.Id}");
            Console.WriteLine($"Provider:        {repo.Provider}");
            Console.WriteLine($"Clone URL:       {repo.CloneUrl}");
            Console.WriteLine($"Default branch:  {repo.DefaultBranch}");
            Console.WriteLine($"Owner:           {repo.OwnerUserId}");
            Console.WriteLine($"Azure identity:  {(repo.HasAzureIdentity ? "configured" : "none")}");
            Console.WriteLine($"Code index:      {repo.CodeIndexStatus}");
            Console.WriteLine($"Created:         {repo.CreatedAt:u}");
        });
        return cmd;
    }

    private static Command BuildDelete(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var idArg = new Argument<string>("id", "Repository ID (GUID)");
        var cmd = new Command("delete", "Delete a repository") { idArg };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForArgument(idArg);
            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            await api.DeleteAsync($"api/repositories/{id}");
            Console.WriteLine("Repository deleted.");
        });
        return cmd;
    }

    private static Command BuildSyncGitHub(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var reposArg = new Argument<string>("repos", "Comma-separated GitHub repo full names (owner/repo)");
        var cmd = new Command("sync-github", "Sync repositories from GitHub") { reposArg };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var repos = ctx.ParseResult.GetValueForArgument(reposArg);
            var ids = repos.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var result = await api.PostAsync<SyncResult>("api/repositories/sync-github",
                new SyncGitHubRepositoriesRequest(ids));
            if (result is null) return;

            Console.WriteLine($"Added: {result.Added}, Updated: {result.Updated}, Skipped: {result.Skipped}");
            foreach (var err in result.Errors)
                Console.Error.WriteLine($"  Error: {err}");
        });
        return cmd;
    }

    private static Command BuildSyncAzdo(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var orgArg = new Argument<string>("organization", "Azure DevOps organization name");
        var reposArg = new Argument<string>("repo-ids", "Comma-separated Azure DevOps repository IDs");
        var projectOption = new Option<string?>("--project", "Azure DevOps project name");
        var cmd = new Command("sync-azdo", "Sync repositories from Azure DevOps") { orgArg, reposArg, projectOption };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var org = ctx.ParseResult.GetValueForArgument(orgArg);
            var repoIds = ctx.ParseResult.GetValueForArgument(reposArg)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var project = ctx.ParseResult.GetValueForOption(projectOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var result = await api.PostAsync<SyncResult>("api/repositories/sync-azure",
                new SyncAzureDevOpsRepositoriesRequest(org, project, repoIds));
            if (result is null) return;

            Console.WriteLine($"Added: {result.Added}, Updated: {result.Updated}, Skipped: {result.Skipped}");
            foreach (var err in result.Errors)
                Console.Error.WriteLine($"  Error: {err}");
        });
        return cmd;
    }

    private static Command BuildShare(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var idArg = new Argument<string>("id", "Repository ID (GUID)");
        var emailArg = new Argument<string>("email", "Email of user to share with");
        var cmd = new Command("share", "Share a repository with a user") { idArg, emailArg };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForArgument(idArg);
            var email = ctx.ParseResult.GetValueForArgument(emailArg);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var result = await api.PostAsync<RepositoryShareDto>($"api/repositories/{id}/share",
                new ShareRepositoryRequest(email));
            if (result is null) return;

            Console.WriteLine($"Shared with {result.SharedWithUserId} (granted at {result.GrantedAt:u}).");
        });
        return cmd;
    }

    private static Command BuildSetLlm(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var idArg = new Argument<string>("id", "Repository ID (GUID)");
        var llmIdArg = new Argument<string?>("llm-setting-id", () => null, "LLM setting ID (GUID), or omit to clear");
        var cmd = new Command("set-llm", "Set or clear the LLM override for a repository") { idArg, llmIdArg };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForArgument(idArg);
            var llmId = ctx.ParseResult.GetValueForArgument(llmIdArg);
            Guid? parsed = string.IsNullOrEmpty(llmId) ? null : Guid.Parse(llmId);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            await api.PatchAsync($"api/repositories/{id}/llm-setting", new UpdateRepositoryLlmRequest(parsed));
            Console.WriteLine(parsed.HasValue ? "LLM setting updated." : "LLM setting cleared.");
        });
        return cmd;
    }

    private static Command BuildSetAzureIdentity(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var idArg = new Argument<string>("id", "Repository ID (GUID)");
        var clientIdOption = new Option<string>("--client-id", "Azure AD application (client) ID") { IsRequired = true };
        var clientSecretOption = new Option<string>("--client-secret", "Azure AD client secret") { IsRequired = true };
        var tenantIdOption = new Option<string>("--tenant-id", "Azure AD tenant ID") { IsRequired = true };
        var subOption = new Option<string?>("--subscription-id", "Azure subscription ID");

        var cmd = new Command("set-azure-identity", "Set Azure Service Principal credentials")
        {
            idArg, clientIdOption, clientSecretOption, tenantIdOption, subOption
        };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForArgument(idArg);
            var clientId = ctx.ParseResult.GetValueForOption(clientIdOption)!;
            var clientSecret = ctx.ParseResult.GetValueForOption(clientSecretOption)!;
            var tenantId = ctx.ParseResult.GetValueForOption(tenantIdOption)!;
            var sub = ctx.ParseResult.GetValueForOption(subOption);

            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            await api.PatchAsync($"api/repositories/{id}/azure-identity",
                new UpdateRepositoryAzureIdentityRequest(clientId, clientSecret, tenantId, sub));
            Console.WriteLine("Azure identity updated.");
        });
        return cmd;
    }

    private static Command BuildVerifyAzureIdentity(Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var idArg = new Argument<string>("id", "Repository ID (GUID)");
        var cmd = new Command("verify-azure-identity", "Verify Azure identity credentials") { idArg };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForArgument(idArg);
            using var api = CreateClient(ctx, apiUrlOption, tokenOption);
            var result = await api.PostAsync<VerifyResult>($"api/repositories/{id}/verify-azure-identity");
            if (result is null) return;

            Console.WriteLine(result.Success ? $"OK: {result.Message}" : $"FAIL: {result.Message}");
            if (!result.Success)
                ctx.ExitCode = 1;
        });
        return cmd;
    }

    private static ApiClient CreateClient(InvocationContext ctx, Option<string> apiUrlOption, Option<string?> tokenOption)
    {
        var apiUrl = ctx.ParseResult.GetValueForOption(apiUrlOption)!;
        var token = ctx.ParseResult.GetValueForOption(tokenOption);
        return new ApiClient(apiUrl, token);
    }

    private record VerifyResult(bool Success, string Message);
}
