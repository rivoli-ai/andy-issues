// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Mapping;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Domain.Services;
using Xunit;

namespace Andy.Issues.Tests.Unit.Mapping;

public class BacklogMappingTests
{
    [Fact]
    public void ToBacklogDto_ReturnsOrderedHierarchy()
    {
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "owner",
            Name = "demo",
            CloneUrl = "https://example.com/demo.git"
        };

        var epic2 = new Epic { Id = Guid.NewGuid(), Title = "Epic 2", Order = 2 };
        var epic1 = new Epic { Id = Guid.NewGuid(), Title = "Epic 1", Order = 1 };
        var feature = new Feature { Id = Guid.NewGuid(), Title = "F1", Order = 1 };
        var storyB = new UserStory { Id = Guid.NewGuid(), Title = "B", Order = 2 };
        var storyA = new UserStory { Id = Guid.NewGuid(), Title = "A", Order = 1 };

        feature.Stories.Add(storyB);
        feature.Stories.Add(storyA);
        epic1.Features.Add(feature);
        repo.Epics.Add(epic2);
        repo.Epics.Add(epic1);

        var dto = repo.ToBacklogDto();

        Assert.Equal(2, dto.Epics.Count);
        Assert.Equal("Epic 1", dto.Epics[0].Title);
        Assert.Equal("Epic 2", dto.Epics[1].Title);

        var mappedFeature = Assert.Single(dto.Epics[0].Features);
        Assert.Equal(2, mappedFeature.Stories.Count);
        Assert.Equal("A", mappedFeature.Stories[0].Title);
        Assert.Equal("B", mappedFeature.Stories[1].Title);
    }

    [Fact]
    public void UserStoryDto_StatusIsStringified()
    {
        var story = new UserStory { Title = "S" };
        story.SetStatus(UserStoryStatus.InReview);
        var dto = story.ToDto();
        Assert.Equal("InReview", dto.Status);
    }

    [Fact]
    public void UserStoryDto_PrefersPersistedContentHashWhenPresent()
    {
        // SP.7.1 — when the entity already carries a stored hash (i.e.
        // it was populated by the SaveChanges hook), the mapping returns
        // that value unchanged rather than recomputing.
        var story = new UserStory
        {
            Title = "T",
            Description = "B",
            ContentHash = "deadbeef" + new string('0', 56)
        };
        var dto = story.ToDto();
        Assert.Equal(story.ContentHash, dto.ContentHash);
    }

    [Fact]
    public void UserStoryDto_FallsBackToComputedHashWhenColumnIsNull()
    {
        // SP.7.1 — pre-migration rows have ContentHash == NULL on disk.
        // The mapping must hand consumers the right hash anyway by
        // computing on the fly.
        var story = new UserStory
        {
            Title = "Legacy",
            Description = "Pre-migration",
            Labels = new List<string> { "bug" },
            AcceptanceCriteria = "AC",
            ContentHash = null
        };
        var dto = story.ToDto();
        Assert.NotNull(dto.ContentHash);
        Assert.Equal(
            StoryContentHasher.Compute("Legacy", "Pre-migration", new[] { "bug" }, "AC"),
            dto.ContentHash);
    }
}
