// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Andy.Issues.Infrastructure.Data;

namespace Andy.Issues.Infrastructure.Services;

public sealed class AiConfigService(AppDbContext db, ILlmSecretStore secrets) : IAiConfigService
{
    public async Task<AiConfigDto?> GetAsync(string ownerId, Guid? repositoryId, CancellationToken ct)
    {
        Guid? settingId = null;
        if (repositoryId.HasValue)
        {
            var repository = await db.Repositories.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == repositoryId && x.OwnerUserId == ownerId, ct);
            if (repository is null) return null;
            settingId = repository.LlmSettingId;
        }
        var settings = db.LlmSettings.AsNoTracking().Where(x => x.OwnerUserId == ownerId);
        var setting = settingId.HasValue
            ? await settings.FirstOrDefaultAsync(x => x.Id == settingId, ct)
            : await settings.Where(x => x.IsDefault).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (setting is null) return null;
        var key = setting.ApiKey.Length == 0 ? "" : await secrets.ResolveAsync(setting.ApiKey, ct);
        if (key is null || (key.Length == 0 && setting.ApiKey.Length != 0))
            throw new InvalidOperationException("AI credential unavailable.");
        return new AiConfigDto
        {
            Provider = setting.Provider.ToString().ToLowerInvariant(),
            ApiKey = key,
            Model = setting.Model,
            BaseUrl = setting.BaseUrl
        };
    }
}
