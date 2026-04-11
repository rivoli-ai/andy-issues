// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Mapping;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Andy.Issues.Infrastructure.Services;

public class BacklogService : IBacklogService
{
    private readonly AppDbContext _db;
    private readonly IRepositoryAccessGuard _guard;

    public BacklogService(AppDbContext db, IRepositoryAccessGuard guard)
    {
        _db = db;
        _guard = guard;
    }

    public async Task<BacklogDto?> GetAsync(Guid repositoryId, string userId, CancellationToken ct = default)
    {
        if (!await _guard.CanViewAsync(repositoryId, userId, ct))
            return null;

        var repo = await _db.Repositories
            .AsNoTracking()
            .Include(r => r.Epics).ThenInclude(e => e.Features).ThenInclude(f => f.Stories)
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct);

        return repo?.ToBacklogDto();
    }

    public async Task<EpicDto?> AddEpicAsync(
        Guid repositoryId,
        CreateEpicRequest request,
        string userId,
        CancellationToken ct = default)
    {
        if (!await _guard.CanViewAsync(repositoryId, userId, ct))
            return null;

        var order = request.Order ?? await NextEpicOrderAsync(repositoryId, ct);
        var epic = new Epic
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            Title = request.Title,
            Description = request.Description,
            Order = order,
            ExternalId = request.ExternalId
        };
        _db.Epics.Add(epic);
        await _db.SaveChangesAsync(ct);
        return epic.ToDto();
    }

    public async Task<EpicDto?> UpdateEpicAsync(
        Guid epicId,
        UpdateEpicRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var epic = await _db.Epics
            .Include(e => e.Features).ThenInclude(f => f.Stories)
            .FirstOrDefaultAsync(e => e.Id == epicId, ct);
        if (epic is null) return null;
        if (!await _guard.CanViewAsync(epic.RepositoryId, userId, ct)) return null;

        if (request.Title is not null) epic.Title = request.Title;
        if (request.Description is not null) epic.Description = request.Description;
        if (request.Order is not null) epic.Order = request.Order.Value;
        epic.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return epic.ToDto();
    }

    public async Task<bool> DeleteEpicAsync(Guid epicId, string userId, CancellationToken ct = default)
    {
        var epic = await _db.Epics.FirstOrDefaultAsync(e => e.Id == epicId, ct);
        if (epic is null) return false;
        if (!await _guard.CanViewAsync(epic.RepositoryId, userId, ct)) return false;

        _db.Epics.Remove(epic);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<FeatureDto?> AddFeatureAsync(
        Guid epicId,
        CreateFeatureRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var epic = await _db.Epics.AsNoTracking().FirstOrDefaultAsync(e => e.Id == epicId, ct);
        if (epic is null) return null;
        if (!await _guard.CanViewAsync(epic.RepositoryId, userId, ct)) return null;

        var order = request.Order ?? await NextFeatureOrderAsync(epicId, ct);
        var feature = new Feature
        {
            Id = Guid.NewGuid(),
            EpicId = epicId,
            Title = request.Title,
            Description = request.Description,
            Order = order,
            ExternalId = request.ExternalId
        };
        _db.Features.Add(feature);
        await _db.SaveChangesAsync(ct);
        return feature.ToDto();
    }

    public async Task<FeatureDto?> UpdateFeatureAsync(
        Guid featureId,
        UpdateFeatureRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var feature = await _db.Features
            .Include(f => f.Stories)
            .Include(f => f.Epic)
            .FirstOrDefaultAsync(f => f.Id == featureId, ct);
        if (feature is null) return null;
        if (!await _guard.CanViewAsync(feature.Epic.RepositoryId, userId, ct)) return null;

        if (request.Title is not null) feature.Title = request.Title;
        if (request.Description is not null) feature.Description = request.Description;
        if (request.Order is not null) feature.Order = request.Order.Value;
        feature.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return feature.ToDto();
    }

    public async Task<bool> DeleteFeatureAsync(Guid featureId, string userId, CancellationToken ct = default)
    {
        var feature = await _db.Features.Include(f => f.Epic).FirstOrDefaultAsync(f => f.Id == featureId, ct);
        if (feature is null) return false;
        if (!await _guard.CanViewAsync(feature.Epic.RepositoryId, userId, ct)) return false;

        _db.Features.Remove(feature);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<UserStoryDto?> AddStoryAsync(
        Guid featureId,
        CreateUserStoryRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var feature = await _db.Features
            .AsNoTracking()
            .Include(f => f.Epic)
            .FirstOrDefaultAsync(f => f.Id == featureId, ct);
        if (feature is null) return null;
        if (!await _guard.CanViewAsync(feature.Epic.RepositoryId, userId, ct)) return null;

        var order = request.Order ?? await NextStoryOrderAsync(featureId, ct);
        var story = new UserStory
        {
            Id = Guid.NewGuid(),
            FeatureId = featureId,
            Title = request.Title,
            Description = request.Description,
            AcceptanceCriteria = request.AcceptanceCriteria,
            StoryPoints = request.StoryPoints,
            Order = order,
            ExternalId = request.ExternalId
        };
        _db.UserStories.Add(story);
        await _db.SaveChangesAsync(ct);
        return story.ToDto();
    }

    public async Task<UserStoryDto?> UpdateStoryAsync(
        Guid storyId,
        UpdateUserStoryRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var story = await _db.UserStories
            .Include(s => s.Feature).ThenInclude(f => f.Epic)
            .FirstOrDefaultAsync(s => s.Id == storyId, ct);
        if (story is null) return null;
        if (!await _guard.CanViewAsync(story.Feature.Epic.RepositoryId, userId, ct)) return null;

        if (request.Title is not null) story.Title = request.Title;
        if (request.Description is not null) story.Description = request.Description;
        if (request.AcceptanceCriteria is not null) story.AcceptanceCriteria = request.AcceptanceCriteria;
        if (request.StoryPoints is not null) story.StoryPoints = request.StoryPoints;
        if (request.Order is not null) story.Order = request.Order.Value;
        story.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return story.ToDto();
    }

    public async Task<UserStoryStatusUpdateResult> UpdateStoryStatusAsync(
        Guid storyId,
        UpdateUserStoryStatusRequest request,
        string userId,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<UserStoryStatus>(request.Status, ignoreCase: true, out var next))
            return UserStoryStatusUpdateResult.InvalidStatus(request.Status);

        var story = await _db.UserStories
            .Include(s => s.Feature).ThenInclude(f => f.Epic)
            .FirstOrDefaultAsync(s => s.Id == storyId, ct);
        if (story is null) return UserStoryStatusUpdateResult.NotFound();
        if (!await _guard.CanViewAsync(story.Feature.Epic.RepositoryId, userId, ct))
            return UserStoryStatusUpdateResult.NotFound();

        try
        {
            story.SetStatus(next);
        }
        catch (InvalidOperationException ex)
        {
            return UserStoryStatusUpdateResult.InvalidTransition(ex.Message);
        }

        if (request.PullRequestUrl is not null)
            story.PullRequestUrl = request.PullRequestUrl;

        await _db.SaveChangesAsync(ct);
        return UserStoryStatusUpdateResult.Ok(story.ToDto());
    }

    public async Task<bool> DeleteStoryAsync(Guid storyId, string userId, CancellationToken ct = default)
    {
        var story = await _db.UserStories
            .Include(s => s.Feature).ThenInclude(f => f.Epic)
            .FirstOrDefaultAsync(s => s.Id == storyId, ct);
        if (story is null) return false;
        if (!await _guard.CanViewAsync(story.Feature.Epic.RepositoryId, userId, ct)) return false;

        _db.UserStories.Remove(story);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<int> NextEpicOrderAsync(Guid repositoryId, CancellationToken ct)
    {
        var max = await _db.Epics
            .AsNoTracking()
            .Where(e => e.RepositoryId == repositoryId)
            .Select(e => (int?)e.Order)
            .MaxAsync(ct) ?? 0;
        return max + 1;
    }

    private async Task<int> NextFeatureOrderAsync(Guid epicId, CancellationToken ct)
    {
        var max = await _db.Features
            .AsNoTracking()
            .Where(f => f.EpicId == epicId)
            .Select(f => (int?)f.Order)
            .MaxAsync(ct) ?? 0;
        return max + 1;
    }

    private async Task<int> NextStoryOrderAsync(Guid featureId, CancellationToken ct)
    {
        var max = await _db.UserStories
            .AsNoTracking()
            .Where(s => s.FeatureId == featureId)
            .Select(s => (int?)s.Order)
            .MaxAsync(ct) ?? 0;
        return max + 1;
    }
}
