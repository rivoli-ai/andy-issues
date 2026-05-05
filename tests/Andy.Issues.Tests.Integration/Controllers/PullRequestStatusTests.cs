// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.PullRequests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Andy.Issues.Tests.Integration.Controllers;

// #88 + #89 — sync-pr-statuses (batch) and stories/{id}/pr-status (single).
public class PullRequestStatusTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PullRequestStatusTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(Guid repoId, Guid storyId)> SeedStoryAsync(
        string ownerUserId, string? prUrl, UserStoryStatus status = UserStoryStatus.InProgress)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Name = "pr-repo",
            CloneUrl = $"https://example.com/{Guid.NewGuid():N}.git"
        };
        db.Repositories.Add(repo);
        var epic = new Epic { Id = Guid.NewGuid(), RepositoryId = repo.Id, Title = "E", Order = 1 };
        db.Epics.Add(epic);
        var feature = new Feature { Id = Guid.NewGuid(), EpicId = epic.Id, Title = "F", Order = 1 };
        db.Features.Add(feature);
        var story = new UserStory
        {
            Id = Guid.NewGuid(),
            FeatureId = feature.Id,
            Title = "story-pr",
            PullRequestUrl = prUrl,
            Order = 1
        };
        if (status != UserStoryStatus.Draft)
            story.SetStatus(status);
        db.UserStories.Add(story);
        await db.SaveChangesAsync();
        return (repo.Id, story.Id);
    }

    private async Task SetGitHubLinkedProviderAsync(string userId, string token = "ghp_test")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.LinkedProviders
            .FirstOrDefaultAsync(p => p.OwnerUserId == userId && p.Provider == LinkedProviderKind.GitHub);
        if (existing is not null) db.LinkedProviders.Remove(existing);
        db.LinkedProviders.Add(new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userId,
            Provider = LinkedProviderKind.GitHub,
            AccessToken = token
        });
        await db.SaveChangesAsync();
    }

    // ── #89 single-story check ────────────────────────────────────────

    [Fact]
    public async Task CheckStory_NoPullRequestUrl_ReturnsHasPrFalse()
    {
        var (_, storyId) = await SeedStoryAsync("dev-user", prUrl: null);

        var resp = await _client.GetAsync($"/api/stories/{storyId}/pr-status");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<StoryPullRequestStatusDto>(JsonOptions);
        Assert.NotNull(dto);
        Assert.False(dto!.HasPr);
        Assert.Null(dto.PrUrl);
        Assert.Null(dto.PrState);
        Assert.Null(dto.PrMerged);
        Assert.False(dto.StatusUpdated);
    }

    [Fact]
    public async Task CheckStory_OpenPr_ReturnsStateOpen_NoTransition()
    {
        await SetGitHubLinkedProviderAsync("dev-user");
        _factory.FakeGitHubClient.SetPullRequestStatus(
            "acme", "widget", 7,
            new PullRequestStatusInfo("open", false, null, "feature/x"));
        var (_, storyId) = await SeedStoryAsync(
            "dev-user", "https://github.com/acme/widget/pull/7");

        var resp = await _client.GetAsync($"/api/stories/{storyId}/pr-status");
        var dto = await resp.Content.ReadFromJsonAsync<StoryPullRequestStatusDto>(JsonOptions);
        Assert.True(dto!.HasPr);
        Assert.Equal("open", dto.PrState);
        Assert.False(dto.PrMerged);
        Assert.False(dto.StatusUpdated);
        Assert.Equal("InProgress", dto.StoryStatus);
    }

    [Fact]
    public async Task CheckStory_MergedPr_TransitionsToDone()
    {
        await SetGitHubLinkedProviderAsync("dev-user");
        var mergedAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        _factory.FakeGitHubClient.SetPullRequestStatus(
            "acme", "widget", 11,
            new PullRequestStatusInfo("merged", true, mergedAt, "feature/y"));
        var (_, storyId) = await SeedStoryAsync(
            "dev-user", "https://github.com/acme/widget/pull/11");

        var resp = await _client.GetAsync($"/api/stories/{storyId}/pr-status");
        var dto = await resp.Content.ReadFromJsonAsync<StoryPullRequestStatusDto>(JsonOptions);
        Assert.True(dto!.PrMerged);
        Assert.Equal("merged", dto.PrState);
        Assert.True(dto.StatusUpdated);
        Assert.Equal("Done", dto.StoryStatus);

        // Outbox row was emitted with kind=done so downstream consumers
        // see the same transition signal as a manual status update.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subjectExists = await db.Outbox
            .AnyAsync(o => o.Subject == $"andy.issues.events.story.{storyId}.done");
        Assert.True(subjectExists);
    }

    [Fact]
    public async Task CheckStory_AlreadyDone_DoesNotEmitDuplicateTransition()
    {
        await SetGitHubLinkedProviderAsync("dev-user");
        _factory.FakeGitHubClient.SetPullRequestStatus(
            "acme", "widget", 13,
            new PullRequestStatusInfo("merged", true, DateTimeOffset.UtcNow, "feature/z"));
        var (_, storyId) = await SeedStoryAsync(
            "dev-user",
            "https://github.com/acme/widget/pull/13",
            status: UserStoryStatus.Done);

        var resp = await _client.GetAsync($"/api/stories/{storyId}/pr-status");
        var dto = await resp.Content.ReadFromJsonAsync<StoryPullRequestStatusDto>(JsonOptions);
        Assert.False(dto!.StatusUpdated);
        Assert.Equal("Done", dto.StoryStatus);
    }

    [Fact]
    public async Task CheckStory_NotOwnedAndNotShared_Returns404()
    {
        var (_, storyId) = await SeedStoryAsync("other-user", prUrl: null);
        var resp = await _client.GetAsync($"/api/stories/{storyId}/pr-status");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task CheckStory_NoLinkedProvider_ReturnsHasPrButNullState()
    {
        // No GitHub LinkedProvider for dev-user — caller can't query
        // the upstream API. Endpoint still returns HasPr/prUrl so the
        // UI can surface "link a PAT to check status".
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LinkedProviders.RemoveRange(
                db.LinkedProviders.Where(p =>
                    p.OwnerUserId == "dev-user" && p.Provider == LinkedProviderKind.GitHub));
            await db.SaveChangesAsync();
        }

        var (_, storyId) = await SeedStoryAsync(
            "dev-user", "https://github.com/acme/widget/pull/77");

        var resp = await _client.GetAsync($"/api/stories/{storyId}/pr-status");
        var dto = await resp.Content.ReadFromJsonAsync<StoryPullRequestStatusDto>(JsonOptions);
        Assert.True(dto!.HasPr);
        Assert.Null(dto.PrState);
        Assert.False(dto.StatusUpdated);
    }

    // ── #88 batch sync ────────────────────────────────────────────────

    [Fact]
    public async Task SyncRepository_TransitionsAllMergedStories()
    {
        await SetGitHubLinkedProviderAsync("dev-user");

        Guid repoId;
        Guid story1Id, story2Id, story3Id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var repo = new Repository
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "dev-user",
                Name = "batch-repo",
                CloneUrl = "https://example.com/batch.git"
            };
            db.Repositories.Add(repo);
            var epic = new Epic { Id = Guid.NewGuid(), RepositoryId = repo.Id, Title = "E", Order = 1 };
            db.Epics.Add(epic);
            var feature = new Feature { Id = Guid.NewGuid(), EpicId = epic.Id, Title = "F", Order = 1 };
            db.Features.Add(feature);

            var s1 = new UserStory
            {
                Id = Guid.NewGuid(),
                FeatureId = feature.Id,
                Title = "merged-pr",
                PullRequestUrl = "https://github.com/acme/widget/pull/100",
                Order = 1
            };
            s1.SetStatus(UserStoryStatus.InProgress);
            var s2 = new UserStory
            {
                Id = Guid.NewGuid(),
                FeatureId = feature.Id,
                Title = "open-pr",
                PullRequestUrl = "https://github.com/acme/widget/pull/101",
                Order = 2
            };
            s2.SetStatus(UserStoryStatus.InProgress);
            var s3 = new UserStory
            {
                Id = Guid.NewGuid(),
                FeatureId = feature.Id,
                Title = "no-pr",
                PullRequestUrl = null,
                Order = 3
            };
            db.UserStories.AddRange(s1, s2, s3);
            await db.SaveChangesAsync();
            repoId = repo.Id;
            story1Id = s1.Id;
            story2Id = s2.Id;
            story3Id = s3.Id;
        }

        _factory.FakeGitHubClient.SetPullRequestStatus(
            "acme", "widget", 100,
            new PullRequestStatusInfo("merged", true, DateTimeOffset.UtcNow, "feature/100"));
        _factory.FakeGitHubClient.SetPullRequestStatus(
            "acme", "widget", 101,
            new PullRequestStatusInfo("open", false, null, "feature/101"));

        var resp = await _client.PostAsync($"/api/repositories/{repoId}/sync-pr-statuses", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = await resp.Content.ReadFromJsonAsync<SyncPullRequestStatusesResultDto>(JsonOptions);
        Assert.True(result!.Success);
        Assert.Equal(repoId, result.RepositoryId);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Single(result.UpdatedStories);
        var update = result.UpdatedStories[0];
        Assert.Equal(story1Id, update.StoryId);
        Assert.Equal("InProgress", update.OldStatus);
        Assert.Equal("Done", update.NewStatus);
        Assert.True(update.PrMerged);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s1Status = await db.UserStories.AsNoTracking()
                .Where(s => s.Id == story1Id).Select(s => s.Status).FirstAsync();
            var s2Status = await db.UserStories.AsNoTracking()
                .Where(s => s.Id == story2Id).Select(s => s.Status).FirstAsync();
            var s3Status = await db.UserStories.AsNoTracking()
                .Where(s => s.Id == story3Id).Select(s => s.Status).FirstAsync();
            Assert.Equal(UserStoryStatus.Done, s1Status);
            Assert.Equal(UserStoryStatus.InProgress, s2Status);
            // s3 has no PR — never seen by the service; stays at the default.
            Assert.Equal(UserStoryStatus.Draft, s3Status);
        }
    }

    [Fact]
    public async Task SyncRepository_UnknownRepo_Returns404()
    {
        var resp = await _client.PostAsync(
            $"/api/repositories/{Guid.NewGuid()}/sync-pr-statuses", content: null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task SyncRepository_BadPrUrl_DoesNotAbortBatch()
    {
        await SetGitHubLinkedProviderAsync("dev-user");

        Guid repoId;
        Guid goodStoryId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var repo = new Repository
            {
                Id = Guid.NewGuid(),
                OwnerUserId = "dev-user",
                Name = "robust-repo",
                CloneUrl = "https://example.com/robust.git"
            };
            db.Repositories.Add(repo);
            var epic = new Epic { Id = Guid.NewGuid(), RepositoryId = repo.Id, Title = "E", Order = 1 };
            db.Epics.Add(epic);
            var feature = new Feature { Id = Guid.NewGuid(), EpicId = epic.Id, Title = "F", Order = 1 };
            db.Features.Add(feature);
            var bad = new UserStory
            {
                Id = Guid.NewGuid(),
                FeatureId = feature.Id,
                Title = "garbage-url",
                PullRequestUrl = "not a real url",
                Order = 1
            };
            bad.SetStatus(UserStoryStatus.InProgress);
            var good = new UserStory
            {
                Id = Guid.NewGuid(),
                FeatureId = feature.Id,
                Title = "real-url",
                PullRequestUrl = "https://github.com/acme/widget/pull/200",
                Order = 2
            };
            good.SetStatus(UserStoryStatus.InProgress);
            db.UserStories.AddRange(bad, good);
            await db.SaveChangesAsync();
            repoId = repo.Id;
            goodStoryId = good.Id;
        }

        _factory.FakeGitHubClient.SetPullRequestStatus(
            "acme", "widget", 200,
            new PullRequestStatusInfo("merged", true, DateTimeOffset.UtcNow, "feature/200"));

        var resp = await _client.PostAsync($"/api/repositories/{repoId}/sync-pr-statuses", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = await resp.Content.ReadFromJsonAsync<SyncPullRequestStatusesResultDto>(JsonOptions);
        Assert.Equal(1, result!.UpdatedCount);
        Assert.Equal(goodStoryId, result.UpdatedStories[0].StoryId);
    }
}
