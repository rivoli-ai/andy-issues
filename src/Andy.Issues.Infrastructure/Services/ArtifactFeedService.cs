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

public class ArtifactFeedService : IArtifactFeedService
{
    private readonly AppDbContext _db;
    private readonly IAzureDevOpsClient? _azureDevOps;

    public ArtifactFeedService(AppDbContext db, IAzureDevOpsClient? azureDevOps = null)
    {
        _db = db;
        _azureDevOps = azureDevOps;
    }

    public async Task<IReadOnlyList<ArtifactFeedConfigDto>> GetEnabledAsync(CancellationToken ct = default)
    {
        var rows = await _db.ArtifactFeedConfigs
            .AsNoTracking()
            .Where(f => f.Enabled)
            .OrderBy(f => f.Name)
            .ToListAsync(ct);
        return rows.Select(f => f.ToDto()).ToList();
    }

    public async Task<IReadOnlyList<ArtifactFeedConfigDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.ArtifactFeedConfigs
            .AsNoTracking()
            .OrderBy(f => f.Name)
            .ToListAsync(ct);
        return rows.Select(f => f.ToDto()).ToList();
    }

    public async Task<ArtifactFeedConfigDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.ArtifactFeedConfigs.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        return row?.ToDto();
    }

    public async Task<ArtifactFeedResult> CreateAsync(
        CreateArtifactFeedConfigRequest request,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<ArtifactFeedType>(request.Type, ignoreCase: true, out var type))
            return Invalid($"Unknown type '{request.Type}'. Use 'nuget', 'npm', or 'pip'.");

        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Organization) ||
            string.IsNullOrWhiteSpace(request.FeedName))
            return Invalid("Name, Organization and FeedName are required.");

        var duplicate = await _db.ArtifactFeedConfigs
            .AnyAsync(f => f.Name == request.Name, ct);
        if (duplicate)
            return new ArtifactFeedResult(
                ArtifactFeedOutcome.Conflict,
                null,
                $"An artifact feed named '{request.Name}' already exists.");

        var entity = new ArtifactFeedConfig
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Organization = request.Organization,
            FeedName = request.FeedName,
            Project = request.Project,
            Type = type,
            Enabled = true
        };
        _db.ArtifactFeedConfigs.Add(entity);
        await _db.SaveChangesAsync(ct);
        return new ArtifactFeedResult(ArtifactFeedOutcome.Ok, entity.ToDto(), null);
    }

    public async Task<ArtifactFeedResult> UpdateAsync(
        Guid id,
        UpdateArtifactFeedConfigRequest request,
        CancellationToken ct = default)
    {
        var row = await _db.ArtifactFeedConfigs.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (row is null)
            return new ArtifactFeedResult(ArtifactFeedOutcome.NotFound, null, null);

        if (request.Name is not null) row.Name = request.Name;
        if (request.Project is not null) row.Project = request.Project;
        if (request.Enabled is not null) row.Enabled = request.Enabled.Value;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new ArtifactFeedResult(ArtifactFeedOutcome.Ok, row.ToDto(), null);
    }

    public async Task<ArtifactFeedOutcome> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.ArtifactFeedConfigs.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (row is null) return ArtifactFeedOutcome.NotFound;
        _db.ArtifactFeedConfigs.Remove(row);
        await _db.SaveChangesAsync(ct);
        return ArtifactFeedOutcome.Ok;
    }

    public async Task<ArtifactFeedBrowseResult> BrowseAzureDevOpsFeedsAsync(
        string userId,
        string organization,
        CancellationToken ct = default)
    {
        if (_azureDevOps is null)
            return new ArtifactFeedBrowseResult(
                ArtifactFeedBrowseOutcome.ProviderError,
                null,
                "Azure DevOps client not configured.");

        if (string.IsNullOrWhiteSpace(organization))
            return new ArtifactFeedBrowseResult(
                ArtifactFeedBrowseOutcome.ProviderError,
                null,
                "Organization is required.");

        var provider = await _db.LinkedProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OwnerUserId == userId && p.Provider == LinkedProviderKind.AzureDevOps, ct);
        if (provider is null)
            return new ArtifactFeedBrowseResult(
                ArtifactFeedBrowseOutcome.NoLinkedProvider,
                null,
                "No Azure DevOps linked provider for the caller.");

        try
        {
            var feeds = await _azureDevOps.ListFeedsAsync(organization, provider.AccessToken, ct);
            return new ArtifactFeedBrowseResult(ArtifactFeedBrowseOutcome.Ok, feeds, null);
        }
        catch (Exception ex)
        {
            return new ArtifactFeedBrowseResult(ArtifactFeedBrowseOutcome.ProviderError, null, ex.Message);
        }
    }

    private static ArtifactFeedResult Invalid(string reason) =>
        new(ArtifactFeedOutcome.Invalid, null, reason);
}
