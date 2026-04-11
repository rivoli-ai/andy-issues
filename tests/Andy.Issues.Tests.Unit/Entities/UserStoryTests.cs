// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Xunit;

namespace Andy.Issues.Tests.Unit.Entities;

public class UserStoryTests
{
    [Fact]
    public void SetStatus_FromDraftToReady_Succeeds()
    {
        var story = new UserStory { Title = "Test" };
        story.SetStatus(UserStoryStatus.Ready);
        Assert.Equal(UserStoryStatus.Ready, story.Status);
        Assert.NotNull(story.UpdatedAt);
    }

    [Fact]
    public void SetStatus_FromDoneBackToDraft_Throws()
    {
        var story = new UserStory { Title = "Test" };
        story.SetStatus(UserStoryStatus.Ready);
        story.SetStatus(UserStoryStatus.InProgress);
        story.SetStatus(UserStoryStatus.InReview);
        story.SetStatus(UserStoryStatus.Done);

        var ex = Assert.Throws<InvalidOperationException>(
            () => story.SetStatus(UserStoryStatus.Draft));
        Assert.Contains("Done", ex.Message);
    }

    [Fact]
    public void SetStatus_FromDoneToInProgress_Succeeds()
    {
        // Re-opening a Done story is allowed (just not moving back to Draft).
        var story = new UserStory { Title = "Test" };
        story.SetStatus(UserStoryStatus.Done);
        story.SetStatus(UserStoryStatus.InProgress);
        Assert.Equal(UserStoryStatus.InProgress, story.Status);
    }

    [Fact]
    public void NewStory_StartsInDraft()
    {
        var story = new UserStory { Title = "Fresh" };
        Assert.Equal(UserStoryStatus.Draft, story.Status);
    }
}
