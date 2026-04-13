// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Api.Mcp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace Andy.Issues.Tests.Unit.Mcp;

public class HelpToolsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IWebHostEnvironment _env;

    public HelpToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"help-tests-{Guid.NewGuid():N}");
        var helpDir = Path.Combine(_tempDir, "content", "help");
        Directory.CreateDirectory(helpDir);

        File.WriteAllText(Path.Combine(helpDir, "getting-started.md"),
            "---\ntitle: Getting Started\norder: 1\n---\n\n# Getting Started\n\nWelcome to Andy Issues.");

        File.WriteAllText(Path.Combine(helpDir, "repositories.md"),
            "---\ntitle: Repositories\norder: 2\n---\n\n# Repositories\n\nManage your code repositories.");

        _env = new StubWebHostEnvironment(_tempDir);
    }

    [Fact]
    public async Task ListHelpTopics_ReturnsAllTopics()
    {
        var result = await HelpTools.ListHelpTopics(_env);

        Assert.Contains("Getting Started", result);
        Assert.Contains("Repositories", result);
        Assert.Contains("getting-started", result);
        Assert.Contains("repositories", result);
    }

    [Fact]
    public async Task GetHelpTopic_ReturnsTopic_StrippingFrontMatter()
    {
        var result = await HelpTools.GetHelpTopic(_env, "getting-started");

        Assert.Contains("Getting Started", result);
        Assert.Contains("Welcome to Andy Issues", result);
        Assert.DoesNotContain("order: 1", result);
    }

    [Fact]
    public async Task GetHelpTopic_NotFound_ReturnsHelpfulMessage()
    {
        var result = await HelpTools.GetHelpTopic(_env, "nonexistent");

        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ListHelpTopics", result);
    }

    [Fact]
    public async Task SearchHelp_FindsMatchingTopics()
    {
        var result = await HelpTools.SearchHelp(_env, "repositories");

        Assert.Contains("Repositories", result);
    }

    [Fact]
    public async Task SearchHelp_NoMatch_ReturnsMessage()
    {
        var result = await HelpTools.SearchHelp(_env, "xyznonexistent");

        Assert.Contains("No help topics match", result);
    }

    [Fact]
    public async Task ListHelpTopics_NoHelpDir_ReturnsUnavailable()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), $"help-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);
        try
        {
            var env = new StubWebHostEnvironment(emptyDir);
            var result = await HelpTools.ListHelpTopics(env);

            Assert.Contains("No help topics available", result);
        }
        finally
        {
            Directory.Delete(emptyDir, true);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private sealed class StubWebHostEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = contentRoot;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "Andy.Issues.Api";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Development";
    }
}
