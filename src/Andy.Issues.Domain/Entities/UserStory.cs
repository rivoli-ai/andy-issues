// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Domain.Enums;

namespace Andy.Issues.Domain.Entities;

public class UserStory
{
    public Guid Id { get; set; }
    public Guid FeatureId { get; set; }
    public Feature Feature { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public int? StoryPoints { get; set; }
    public UserStoryStatus Status { get; private set; } = UserStoryStatus.Draft;
    public string? PullRequestUrl { get; set; }
    public int Order { get; set; }
    public string? ExternalId { get; set; }
    public int? AzureDevOpsWorkItemId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public void SetStatus(UserStoryStatus next)
    {
        if (Status == UserStoryStatus.Done && next == UserStoryStatus.Draft)
            throw new InvalidOperationException(
                $"Invalid status transition: {Status} → {next}.");

        Status = next;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
