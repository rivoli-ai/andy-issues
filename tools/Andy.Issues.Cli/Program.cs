// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.CommandLine;

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

// Commands will be registered in Epic 11 (repos, backlog, sandbox, mcp, artifact-feeds).

return await rootCommand.InvokeAsync(args);
