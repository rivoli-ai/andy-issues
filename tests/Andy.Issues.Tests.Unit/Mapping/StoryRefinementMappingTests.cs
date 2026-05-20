// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.Services;
using Xunit;

namespace Andy.Issues.Tests.Unit.Mapping;

// SP.0.4 (andy-issues#180 / conductor#1632) — triage-state derivation
// rules. Conductor's RefinementPanel renders one of four panels off
// this state machine:
//
//   • NotTriaged → "Triage this story" button
//   • Triaging   → live progress strip
//   • Triaged    → classification + refinement card
//   • Obsolete   → "Story drifted, re-triage?" prompt
//
// The mapping is the single source of truth for the transition rules.
public class StoryRefinementMappingTests
{
    private static UserStory NewStory() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Story",
        Description = "Body",
        AcceptanceCriteria = "AC",
        Labels = new List<string> { "label" }
    };

    [Fact]
    public void Unrefined_Story_Maps_To_NotTriaged()
    {
        var story = NewStory();
        var state = story.DeriveTriageState(triaging: false);

        Assert.IsType<StoryTriageStateDto.NotTriaged>(state);
    }

    [Fact]
    public void Triaging_Flag_Forces_Triaging_State()
    {
        var story = NewStory();
        // Even if RefinedAt is populated, an explicit triaging flag wins.
        story.RefinedAt = DateTimeOffset.UtcNow;
        story.RefineVersion = 1;

        var state = story.DeriveTriageState(triaging: true);

        Assert.IsType<StoryTriageStateDto.Triaging>(state);
    }

    [Fact]
    public void Refined_Story_Without_Drift_Maps_To_Triaged()
    {
        var story = NewStory();
        story.RefineVersion = 1;
        story.RefinedAt = DateTimeOffset.UtcNow;
        story.RefinedBy = "echo";
        story.ContentHash = StoryContentHasher.Compute(story);
        story.StoryContentHashAtTriage = story.ContentHash;

        var state = story.DeriveTriageState();
        var triaged = Assert.IsType<StoryTriageStateDto.Triaged>(state);
        Assert.Equal(1, triaged.Version);
    }

    [Fact]
    public void Refined_Story_With_Content_Drift_Maps_To_Obsolete()
    {
        var story = NewStory();
        story.RefineVersion = 2;
        story.RefinedAt = DateTimeOffset.UtcNow;
        story.RefinedBy = "echo";
        story.StoryContentHashAtTriage = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        // Live content yields a different hash from the snapshot above.
        story.Title = "Story (updated)";
        story.ContentHash = StoryContentHasher.Compute(story);

        var state = story.DeriveTriageState();
        var obsolete = Assert.IsType<StoryTriageStateDto.Obsolete>(state);
        Assert.Equal(2, obsolete.Version);
    }

    [Fact]
    public void Refined_Story_Without_Hash_Snapshot_Maps_To_Triaged()
    {
        // Legacy row written before SP.0.4 might be refined but missing
        // the StoryContentHashAtTriage column — surface as Triaged
        // rather than guessing drift.
        var story = NewStory();
        story.RefineVersion = 1;
        story.RefinedAt = DateTimeOffset.UtcNow;
        story.RefinedBy = "echo";
        story.StoryContentHashAtTriage = null;
        story.ContentHash = "abc";

        var state = story.DeriveTriageState();
        Assert.IsType<StoryTriageStateDto.Triaged>(state);
    }

    [Fact]
    public void Unrefined_Story_Produces_Null_RefinementDto()
    {
        var dto = NewStory().ToRefinementDto();
        Assert.Null(dto);
    }

    [Fact]
    public void Refined_Story_Produces_Populated_RefinementDto()
    {
        var story = NewStory();
        story.RefineVersion = 1;
        story.RefinedAt = DateTimeOffset.UtcNow;
        story.RefinedBy = "echo";
        story.RefinedDescription = "Refined body";
        story.AcceptanceCriteriaList = new List<string> { "AC1" };
        story.Risks = new List<string> { "R1" };
        story.TestPlan = new List<string> { "T1" };
        story.Priority = StoryPriority.P1;
        story.Complexity = StoryComplexity.Large;
        story.Risk = StoryRisk.High;
        story.SuggestedApproach = "approach";

        var dto = story.ToRefinementDto();

        Assert.NotNull(dto);
        Assert.Equal("Refined body", dto!.RefinedDescription);
        Assert.Single(dto.AcceptanceCriteria);
        Assert.Single(dto.Risks);
        Assert.Single(dto.TestPlan);
        Assert.Equal(StoryPriorityWire.p1, dto.Classification.Priority);
        Assert.Equal(StoryComplexityWire.large, dto.Classification.Complexity);
        Assert.Equal(StoryRiskWire.high, dto.Classification.Risk);
        Assert.Equal("approach", dto.Classification.SuggestedApproach);
        Assert.Equal(1, dto.RefineVersion);
        Assert.Equal("echo", dto.RefinedBy);
    }

    [Theory]
    [InlineData(StoryPriority.P0, StoryPriorityWire.p0)]
    [InlineData(StoryPriority.P1, StoryPriorityWire.p1)]
    [InlineData(StoryPriority.P2, StoryPriorityWire.p2)]
    [InlineData(StoryPriority.P3, StoryPriorityWire.p3)]
    public void ToWire_Priority(StoryPriority domain, StoryPriorityWire wire)
    {
        Assert.Equal(wire, StoryRefinementMapping.ToWire(domain));
    }

    [Theory]
    [InlineData(StoryComplexity.Trivial, StoryComplexityWire.trivial)]
    [InlineData(StoryComplexity.Small, StoryComplexityWire.small)]
    [InlineData(StoryComplexity.Medium, StoryComplexityWire.medium)]
    [InlineData(StoryComplexity.Large, StoryComplexityWire.large)]
    [InlineData(StoryComplexity.Xl, StoryComplexityWire.xl)]
    public void ToWire_Complexity(StoryComplexity domain, StoryComplexityWire wire)
    {
        Assert.Equal(wire, StoryRefinementMapping.ToWire(domain));
    }

    [Theory]
    [InlineData(StoryRisk.Low, StoryRiskWire.low)]
    [InlineData(StoryRisk.Medium, StoryRiskWire.medium)]
    [InlineData(StoryRisk.High, StoryRiskWire.high)]
    public void ToWire_Risk(StoryRisk domain, StoryRiskWire wire)
    {
        Assert.Equal(wire, StoryRefinementMapping.ToWire(domain));
    }
}
