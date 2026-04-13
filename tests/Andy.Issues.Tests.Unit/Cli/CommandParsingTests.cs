// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.CommandLine;
using System.CommandLine.Parsing;
using Andy.Issues.Cli.Commands;
using Xunit;

namespace Andy.Issues.Tests.Unit.Cli;

public class CommandParsingTests
{
    private static readonly Option<string> ApiUrlOption = new("--api-url", () => "https://localhost:5410");
    private static readonly Option<string?> TokenOption = new("--token");

    // ── Repos ───────────────────────────────────────────────────────

    [Fact]
    public void Repos_List_ParsesScope()
    {
        var cmd = ReposCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("list --scope shared");

        Assert.Empty(result.Errors);
        Assert.Equal("list", result.CommandResult.Command.Name);
    }

    [Fact]
    public void Repos_Get_RequiresId()
    {
        var cmd = ReposCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("get");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Repos_Get_ParsesId()
    {
        var cmd = ReposCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("get 00000000-0000-0000-0000-000000000001");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Repos_Delete_RequiresId()
    {
        var cmd = ReposCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("delete");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Repos_SyncGitHub_RequiresRepos()
    {
        var cmd = ReposCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("sync-github");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Repos_SyncGitHub_ParsesRepos()
    {
        var cmd = ReposCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("sync-github owner/repo1,owner/repo2");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Repos_SyncAzdo_RequiresOrgAndRepoIds()
    {
        var cmd = ReposCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("sync-azdo");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Repos_Share_RequiresIdAndEmail()
    {
        var cmd = ReposCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("share");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Repos_SetAzureIdentity_RequiresOptions()
    {
        var cmd = ReposCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("set-azure-identity some-id");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Repos_SetAzureIdentity_ParsesAllOptions()
    {
        var cmd = ReposCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("set-azure-identity some-id --client-id c --client-secret s --tenant-id t --subscription-id sub");

        Assert.Empty(result.Errors);
    }

    // ── Backlog ─────────────────────────────────────────────────────

    [Fact]
    public void Backlog_List_RequiresRepoId()
    {
        var cmd = BacklogCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("list");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Backlog_AddEpic_RequiresRepoIdAndTitle()
    {
        var cmd = BacklogCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("add-epic");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Backlog_AddEpic_ParsesArgs()
    {
        var cmd = BacklogCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("add-epic repo-id \"My Epic\"");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Backlog_AddFeature_RequiresEpicIdAndTitle()
    {
        var cmd = BacklogCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("add-feature");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Backlog_AddStory_RequiresFeatureIdAndTitle()
    {
        var cmd = BacklogCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("add-story");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Backlog_AddStory_ParsesAllOptions()
    {
        var cmd = BacklogCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("add-story feat-id \"My Story\" --description desc --acceptance-criteria ac --points 5");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Backlog_SetStatus_RequiresStoryIdAndStatus()
    {
        var cmd = BacklogCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("set-status");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Backlog_SetStatus_ParsesArgs()
    {
        var cmd = BacklogCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("set-status story-id InProgress");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Backlog_Draft_RequiresRepoId()
    {
        var cmd = BacklogCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("draft");

        Assert.NotEmpty(result.Errors);
    }

    // ── Sandbox ─────────────────────────────────────────────────────

    [Fact]
    public void Sandbox_Create_RequiresRepoIdAndBranch()
    {
        var cmd = SandboxCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("create some-id");

        Assert.NotEmpty(result.Errors); // missing --branch
    }

    [Fact]
    public void Sandbox_Create_ParsesArgs()
    {
        var cmd = SandboxCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("create some-id --branch main");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Sandbox_List_ParsesClean()
    {
        var cmd = SandboxCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("list");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Sandbox_Connect_SupportsOpenFlag()
    {
        var cmd = SandboxCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("connect some-id --open");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Sandbox_Destroy_RequiresId()
    {
        var cmd = SandboxCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("destroy");

        Assert.NotEmpty(result.Errors);
    }

    // ── MCP ─────────────────────────────────────────────────────────

    [Fact]
    public void Mcp_List_ParsesClean()
    {
        var cmd = McpCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("list");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Mcp_Add_RequiresNameAndCommand()
    {
        var cmd = McpCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("add");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Mcp_Add_ParsesArgs()
    {
        var cmd = McpCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("add my-server /usr/local/bin/mcp-server --args [\"--port\",\"8080\"]");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Mcp_AddRemote_RequiresNameAndUrl()
    {
        var cmd = McpCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("add-remote");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Mcp_Toggle_RequiresId()
    {
        var cmd = McpCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("toggle");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Mcp_Discover_RequiresId()
    {
        var cmd = McpCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("discover");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Mcp_Delete_RequiresId()
    {
        var cmd = McpCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("delete");

        Assert.NotEmpty(result.Errors);
    }

    // ── Artifact Feeds ──────────────────────────────────────────────

    [Fact]
    public void ArtifactFeeds_ListEnabled_ParsesClean()
    {
        var cmd = ArtifactFeedsCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("list-enabled");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ArtifactFeeds_AdminList_ParsesClean()
    {
        var cmd = ArtifactFeedsCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("admin list");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ArtifactFeeds_AdminAdd_RequiresArgs()
    {
        var cmd = ArtifactFeedsCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("admin add");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ArtifactFeeds_AdminAdd_ParsesArgs()
    {
        var cmd = ArtifactFeedsCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("admin add \"My Feed\" my-org my-feed --type npm --project my-proj");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ArtifactFeeds_AdminDelete_RequiresId()
    {
        var cmd = ArtifactFeedsCommand.Build(ApiUrlOption, TokenOption);
        var result = cmd.Parse("admin delete");

        Assert.NotEmpty(result.Errors);
    }
}
