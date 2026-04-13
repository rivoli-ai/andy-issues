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

public class LinkedProviderService : ILinkedProviderService
{
    private readonly AppDbContext _db;
    private readonly ISecretStore _secretStore;
    private readonly IGitHubClient _gitHub;
    private readonly IAzureDevOpsClient _azureDevOps;

    public LinkedProviderService(
        AppDbContext db,
        ISecretStore secretStore,
        IGitHubClient gitHub,
        IAzureDevOpsClient azureDevOps)
    {
        _db = db;
        _secretStore = secretStore;
        _gitHub = gitHub;
        _azureDevOps = azureDevOps;
    }

    public async Task<(LinkPatResult Result, LinkedProviderDto? Dto)> LinkPatAsync(
        LinkPatRequest request,
        string ownerUserId,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<LinkedProviderKind>(request.Provider, ignoreCase: true, out var kind))
            return (LinkPatResult.InvalidProvider, null);

        // Validate the PAT by making one API call to the provider
        string? accountLogin;
        switch (kind)
        {
            case LinkedProviderKind.GitHub:
                var ghUser = await _gitHub.GetCurrentUserAsync(request.Pat, ct);
                if (ghUser is null)
                    return (LinkPatResult.InvalidPat, null);
                accountLogin = ghUser.Login;
                break;

            case LinkedProviderKind.AzureDevOps:
                var azUser = await _azureDevOps.GetCurrentUserAsync(request.Pat, ct);
                if (azUser is null)
                    return (LinkPatResult.InvalidPat, null);
                accountLogin = azUser.DisplayName;
                break;

            default:
                return (LinkPatResult.InvalidProvider, null);
        }

        // PAT is valid — upsert via the normal path
        var (_, dto) = await UpsertAsync(
            new CreateLinkedProviderRequest(request.Provider, request.Pat, null, null, accountLogin),
            ownerUserId, ct);

        return (LinkPatResult.Linked, dto);
    }

    public async Task<(UpsertLinkedProviderResult Result, LinkedProviderDto? Dto)> UpsertAsync(
        CreateLinkedProviderRequest request,
        string ownerUserId,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<LinkedProviderKind>(request.Provider, ignoreCase: true, out var kind))
            return (UpsertLinkedProviderResult.InvalidProvider, null);

        var storedToken = await _secretStore.StoreAsync(
            $"andy.issues.user.{ownerUserId}.{kind}.accessToken", request.AccessToken, ct);

        string? storedRefresh = null;
        if (!string.IsNullOrEmpty(request.RefreshToken))
        {
            storedRefresh = await _secretStore.StoreAsync(
                $"andy.issues.user.{ownerUserId}.{kind}.refreshToken", request.RefreshToken, ct);
        }

        var existing = await _db.LinkedProviders
            .FirstOrDefaultAsync(p => p.OwnerUserId == ownerUserId && p.Provider == kind, ct);

        if (existing is not null)
        {
            existing.AccessToken = storedToken;
            existing.RefreshToken = storedRefresh;
            existing.ExpiresAt = request.ExpiresAt;
            existing.AccountLogin = request.AccountLogin;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return (UpsertLinkedProviderResult.Updated, existing.ToDto());
        }

        var entity = new LinkedProvider
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Provider = kind,
            AccessToken = storedToken,
            RefreshToken = storedRefresh,
            ExpiresAt = request.ExpiresAt,
            AccountLogin = request.AccountLogin,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.LinkedProviders.Add(entity);
        await _db.SaveChangesAsync(ct);
        return (UpsertLinkedProviderResult.Created, entity.ToDto());
    }

    public async Task<IReadOnlyList<LinkedProviderDto>> ListAsync(
        string ownerUserId,
        CancellationToken ct = default)
    {
        var providers = await _db.LinkedProviders
            .AsNoTracking()
            .Where(p => p.OwnerUserId == ownerUserId)
            .OrderBy(p => p.Provider)
            .ToListAsync(ct);

        return providers.Select(p => p.ToDto()).ToList();
    }

    public async Task<bool> DeleteAsync(
        string provider,
        string ownerUserId,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<LinkedProviderKind>(provider, ignoreCase: true, out var kind))
            return false;

        var entity = await _db.LinkedProviders
            .FirstOrDefaultAsync(p => p.OwnerUserId == ownerUserId && p.Provider == kind, ct);

        if (entity is null)
            return false;

        _db.LinkedProviders.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
