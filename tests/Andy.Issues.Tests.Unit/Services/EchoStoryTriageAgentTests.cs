// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Triage;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

// SP.0.4 (andy-issues#180) — EchoStoryTriageAgent contract tests.
// The stub is the production default; a real LLM-backed adapter lands
// in the SP.0.13 follow-up. These tests pin the heuristics so the
// Conductor RefinementPanel always renders non-empty output even when
// the LLM dependency is offline.
public class EchoStoryTriageAgentTests
{
    private static StoryTriageAgentInput Input(
        string title = "Add inbox triage",
        string? description = null,
        IReadOnlyList<string>? labels = null,
        string? instructions = null,
        string? acceptanceCriteria = null) =>
        new(
            StoryId: Guid.NewGuid(),
            Title: title,
            Description: description,
            AcceptanceCriteria: acceptanceCriteria,
            Labels: labels ?? Array.Empty<string>(),
            Instructions: instructions,
            AgentId: "echo");

    [Fact]
    public async Task Refine_AlwaysProducesNonEmptyOutput()
    {
        var agent = new EchoStoryTriageAgent();
        var output = await agent.RefineAsync(Input());

        Assert.NotEmpty(output.AcceptanceCriteria);
        Assert.NotEmpty(output.Risks);
        Assert.NotEmpty(output.TestPlan);
        Assert.NotEmpty(output.SuggestedApproach);
        Assert.NotNull(output.RefinedDescription);
    }

    [Theory]
    [InlineData("p0", StoryPriority.P0)]
    [InlineData("P0", StoryPriority.P0)]
    [InlineData("critical", StoryPriority.P0)]
    [InlineData("urgent", StoryPriority.P0)]
    [InlineData("priority:p0-critical", StoryPriority.P0)]
    [InlineData("p1", StoryPriority.P1)]
    [InlineData("high", StoryPriority.P1)]
    [InlineData("p3", StoryPriority.P3)]
    [InlineData("low", StoryPriority.P3)]
    [InlineData("bug", StoryPriority.P2)]
    public async Task Refine_DerivesPriorityFromLabels(string label, StoryPriority expected)
    {
        var agent = new EchoStoryTriageAgent();
        var output = await agent.RefineAsync(Input(labels: new[] { label }));
        Assert.Equal(expected, output.Priority);
    }

    [Fact]
    public async Task Refine_DefaultsPriorityToP2WhenNoLabels()
    {
        var agent = new EchoStoryTriageAgent();
        var output = await agent.RefineAsync(Input());
        Assert.Equal(StoryPriority.P2, output.Priority);
    }

    [Fact]
    public async Task Refine_DerivesComplexityFromBodyLength()
    {
        var agent = new EchoStoryTriageAgent();

        var trivial = await agent.RefineAsync(Input(description: "short"));
        Assert.Equal(StoryComplexity.Trivial, trivial.Complexity);

        var small = await agent.RefineAsync(Input(description: new string('a', 500)));
        Assert.Equal(StoryComplexity.Small, small.Complexity);

        var medium = await agent.RefineAsync(Input(description: new string('a', 1500)));
        Assert.Equal(StoryComplexity.Medium, medium.Complexity);

        var large = await agent.RefineAsync(Input(description: new string('a', 3000)));
        Assert.Equal(StoryComplexity.Large, large.Complexity);

        var xl = await agent.RefineAsync(Input(description: new string('a', 6000)));
        Assert.Equal(StoryComplexity.Xl, xl.Complexity);
    }

    [Fact]
    public async Task Refine_RiskIsMedium()
    {
        var agent = new EchoStoryTriageAgent();
        var output = await agent.RefineAsync(Input());
        Assert.Equal(StoryRisk.Medium, output.Risk);
    }

    [Fact]
    public async Task Refine_EchoesInstructionsIntoRefinedDescription()
    {
        var agent = new EchoStoryTriageAgent();
        var output = await agent.RefineAsync(Input(
            description: "Original body",
            instructions: "Focus on accessibility."));

        Assert.Contains("[Refined by echo]", output.RefinedDescription);
        Assert.Contains("Focus on accessibility", output.RefinedDescription);
    }

    [Fact]
    public async Task Refine_DeterministicAcrossCalls()
    {
        var agent = new EchoStoryTriageAgent();
        var input = Input(title: "Stable title", description: "Stable body");

        var a = await agent.RefineAsync(input);
        var b = await agent.RefineAsync(input);

        Assert.Equal(a.Priority, b.Priority);
        Assert.Equal(a.Complexity, b.Complexity);
        Assert.Equal(a.Risk, b.Risk);
        Assert.Equal(a.SuggestedApproach, b.SuggestedApproach);
        Assert.Equal(a.RefinedDescription, b.RefinedDescription);
    }
}
