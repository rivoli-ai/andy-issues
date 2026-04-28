// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.CommandLine;
using Andy.Issues.Cli;
using Andy.Issues.Cli.Commands;

var rootCommand = new RootCommand("Andy Issues CLI - Manage andy-issues resources");

var apiUrlOption = new Option<string>(
    "--api-url",
    getDefaultValue: () => "https://localhost:5410",
    description: "The Andy Issues API base URL");
rootCommand.AddGlobalOption(apiUrlOption);

var tokenOption = new Option<string?>(
    "--token",
    description: "Bearer token for authentication");
rootCommand.AddGlobalOption(tokenOption);

rootCommand.AddCommand(ReposCommand.Build(apiUrlOption, tokenOption));
rootCommand.AddCommand(BacklogCommand.Build(apiUrlOption, tokenOption));
rootCommand.AddCommand(IssuesCommand.Build(apiUrlOption, tokenOption));
rootCommand.AddCommand(SandboxCommand.Build(apiUrlOption, tokenOption));
rootCommand.AddCommand(McpCommand.Build(apiUrlOption, tokenOption));
rootCommand.AddCommand(ArtifactFeedsCommand.Build(apiUrlOption, tokenOption));

return await rootCommand.InvokeAsync(args);
