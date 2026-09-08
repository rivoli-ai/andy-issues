// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class AiConfigServiceTests
{
    [Theory]
    [InlineData("other", "owner")]
    [InlineData("owner", "other")]
    public async Task ForeignRepositoryOrOverride_IsRejectedBeforeSecretResolution(string repoOwner, string configOwner)
    {
        using var db = Database();
        var setting = new LlmSetting { Id = Guid.NewGuid(), OwnerUserId = configOwner, ApiKey = "protected-value" };
        var repository = new Repository { Id = Guid.NewGuid(), OwnerUserId = repoOwner, LlmSettingId = setting.Id };
        repository.AddShare("owner", repoOwner);
        db.AddRange(setting, repository, new LlmSetting { Id = Guid.NewGuid(), OwnerUserId = "owner", IsDefault = true });
        await db.SaveChangesAsync();
        var secrets = new Secrets();
        Assert.Null(await new AiConfigService(db, secrets).GetAsync("owner", repository.Id, default));
        Assert.Equal(0, secrets.Calls);
    }

    [Fact]
    public async Task OwnedRepositoryWithoutOverride_UsesOnlyOwnerDefault()
    {
        using var db = Database();
        var repository = new Repository { Id = Guid.NewGuid(), OwnerUserId = "owner" };
        db.AddRange(repository,
            new LlmSetting { Id = Guid.NewGuid(), OwnerUserId = "other", IsDefault = true, Model = "foreign" },
            new LlmSetting { Id = Guid.NewGuid(), OwnerUserId = "owner", IsDefault = true, Model = "own", ApiKey = "protected-value" });
        await db.SaveChangesAsync();
        var result = await new AiConfigService(db, new Secrets()).GetAsync("owner", repository.Id, default);
        Assert.Equal("own", result!.Model);
        Assert.Equal("test-resolved-key", result.ApiKey);
        Assert.DoesNotContain(result.ApiKey, result.ToString());
    }

    [Fact]
    public async Task UnreadableStoredKey_FailsClosed()
    {
        using var db = Database();
        db.Add(new LlmSetting { Id = Guid.NewGuid(), OwnerUserId = "owner", IsDefault = true, ApiKey = "unreadable" });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AiConfigService(db, new Secrets(null)).GetAsync("owner", null, default));
    }

    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class Secrets(string? key = "test-resolved-key") : ILlmSecretStore
    {
        public int Calls { get; private set; }
        public Task<string?> ResolveAsync(string? value, CancellationToken ct = default) { Calls++; return Task.FromResult(key); }
        public Task<string> StoreAsync(string name, string value, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
