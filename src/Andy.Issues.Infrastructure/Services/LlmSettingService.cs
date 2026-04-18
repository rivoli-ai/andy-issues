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

public class LlmSettingService : ILlmSettingService
{
    private readonly AppDbContext _db;
    private readonly ISecretStore _secretStore;
    private readonly IBacklogAiService _backlogAi;

    public LlmSettingService(
        AppDbContext db,
        ISecretStore secretStore,
        IBacklogAiService backlogAi)
    {
        _db = db;
        _secretStore = secretStore;
        _backlogAi = backlogAi;
    }

    public async Task<IReadOnlyList<LlmSettingDto>> ListAsync(
        string ownerUserId,
        CancellationToken ct = default)
    {
        var rows = await _db.LlmSettings
            .AsNoTracking()
            .Where(l => l.OwnerUserId == ownerUserId)
            .OrderByDescending(l => l.IsDefault)
            .ThenBy(l => l.Name)
            .ToListAsync(ct);
        return rows.Select(l => l.ToDto()).ToList();
    }

    public async Task<LlmSettingDto?> GetAsync(
        Guid id,
        string ownerUserId,
        CancellationToken ct = default)
    {
        var row = await _db.LlmSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id && l.OwnerUserId == ownerUserId, ct);
        return row?.ToDto();
    }

    public async Task<(CreateLlmSettingResult Result, LlmSettingDto? Dto)> CreateAsync(
        CreateLlmSettingRequest request,
        string ownerUserId,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<LlmProvider>(request.Provider, ignoreCase: true, out var provider))
            return (CreateLlmSettingResult.InvalidProvider, null);

        if (!IsValidBaseUrl(request.BaseUrl))
            return (CreateLlmSettingResult.InvalidBaseUrl, null);

        var entity = new LlmSetting
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Name = request.Name,
            Provider = provider,
            Model = request.Model,
            BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? null : request.BaseUrl,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Store the API key via the secret store and persist only the
        // returned reference. LocalSettingsClient / dev-mode may return
        // the raw string unchanged; that's fine — the column type and
        // the DTO contract still guarantee it never leaves the server.
        entity.ApiKey = await _secretStore.StoreAsync(
            $"andy.issues.user.{ownerUserId}.llm.{entity.Id}.apiKey",
            request.ApiKey,
            ct);

        // If the caller asked for this setting to be default, or it's
        // the first setting they've ever created, promote it and clear
        // any previous default. Using a tracking update (rather than
        // `ExecuteUpdateAsync`) keeps this path compatible with the
        // EF InMemory provider used by integration tests.
        var isFirst = !await _db.LlmSettings.AnyAsync(l => l.OwnerUserId == ownerUserId, ct);
        var shouldBeDefault = request.IsDefault == true || isFirst;
        if (shouldBeDefault)
        {
            await ClearOtherDefaultsAsync(ownerUserId, exceptId: null, ct);
            entity.IsDefault = true;
        }

        _db.LlmSettings.Add(entity);
        await _db.SaveChangesAsync(ct);
        return (CreateLlmSettingResult.Created, entity.ToDto());
    }

    public async Task<(UpdateLlmSettingResult Result, LlmSettingDto? Dto)> UpdateAsync(
        Guid id,
        UpdateLlmSettingRequest request,
        string ownerUserId,
        CancellationToken ct = default)
    {
        var entity = await _db.LlmSettings
            .FirstOrDefaultAsync(l => l.Id == id && l.OwnerUserId == ownerUserId, ct);
        if (entity is null)
            return (UpdateLlmSettingResult.NotFound, null);

        if (request.Provider is not null)
        {
            if (!Enum.TryParse<LlmProvider>(request.Provider, ignoreCase: true, out var provider))
                return (UpdateLlmSettingResult.InvalidProvider, null);
            entity.Provider = provider;
        }

        if (request.BaseUrl is not null && !IsValidBaseUrl(request.BaseUrl))
            return (UpdateLlmSettingResult.InvalidBaseUrl, null);

        if (!string.IsNullOrWhiteSpace(request.Name))
            entity.Name = request.Name;

        if (!string.IsNullOrWhiteSpace(request.Model))
            entity.Model = request.Model;

        if (request.BaseUrl is not null)
            entity.BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? null : request.BaseUrl;

        if (!string.IsNullOrEmpty(request.ApiKey))
        {
            entity.ApiKey = await _secretStore.StoreAsync(
                $"andy.issues.user.{ownerUserId}.llm.{entity.Id}.apiKey",
                request.ApiKey,
                ct);
        }

        if (request.IsDefault == true && !entity.IsDefault)
        {
            await ClearOtherDefaultsAsync(ownerUserId, exceptId: entity.Id, ct);
            entity.IsDefault = true;
        }

        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return (UpdateLlmSettingResult.Updated, entity.ToDto());
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        string ownerUserId,
        CancellationToken ct = default)
    {
        var entity = await _db.LlmSettings
            .FirstOrDefaultAsync(l => l.Id == id && l.OwnerUserId == ownerUserId, ct);
        if (entity is null) return false;

        _db.LlmSettings.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetDefaultAsync(
        Guid id,
        string ownerUserId,
        CancellationToken ct = default)
    {
        var entity = await _db.LlmSettings
            .FirstOrDefaultAsync(l => l.Id == id && l.OwnerUserId == ownerUserId, ct);
        if (entity is null) return false;

        await ClearOtherDefaultsAsync(ownerUserId, exceptId: id, ct);
        entity.IsDefault = true;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(TestLlmSettingOutcome Outcome, string? Message)> TestAsync(
        Guid id,
        string ownerUserId,
        CancellationToken ct = default)
    {
        // The setting carries a secret-store reference in `ApiKey`
        // for encrypted-at-rest deployments. Resolve to the actual
        // key for the live call; the in-memory entity we pass to
        // the AI service has the secret inline.
        var row = await _db.LlmSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id && l.OwnerUserId == ownerUserId, ct);
        if (row is null)
            return (TestLlmSettingOutcome.NotFound, null);

        var resolvedKey = await _secretStore.ResolveAsync(row.ApiKey, ct) ?? row.ApiKey;
        var settingForCall = new LlmSetting
        {
            Id = row.Id,
            OwnerUserId = row.OwnerUserId,
            Name = row.Name,
            Provider = row.Provider,
            Model = row.Model,
            BaseUrl = row.BaseUrl,
            ApiKey = resolvedKey,
            IsDefault = row.IsDefault,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };

        var (success, message) = await _backlogAi.TestConnectionAsync(settingForCall, ct);
        return success
            ? (TestLlmSettingOutcome.Ok, message)
            : (TestLlmSettingOutcome.ProviderRejected, message);
    }

    private async Task ClearOtherDefaultsAsync(string ownerUserId, Guid? exceptId, CancellationToken ct)
    {
        var others = await _db.LlmSettings
            .Where(l => l.OwnerUserId == ownerUserId
                && l.IsDefault
                && (exceptId == null || l.Id != exceptId))
            .ToListAsync(ct);
        foreach (var row in others)
            row.IsDefault = false;
    }

    private static bool IsValidBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return true;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed))
            return false;
        return parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps;
    }
}
