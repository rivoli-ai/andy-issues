// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.CommandLine;
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

// Item commands
var itemsCommand = new Command("items", "Manage items");
ItemCommands.Register(itemsCommand, apiUrlOption, tokenOption);
rootCommand.AddCommand(itemsCommand);

return await rootCommand.InvokeAsync(args);
