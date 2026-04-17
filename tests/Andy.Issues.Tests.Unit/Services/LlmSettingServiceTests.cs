// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class LlmSettingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly RecordingSecretStore _secretStore = new();

    public LlmSettingServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new AppDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext NewContext() => new(_options);
    private LlmSettingService NewService(AppDbContext ctx) => new(ctx, _secretStore);

    // MARK: - Create

    [Fact]
    public async Task Create_PersistsRowAndStoresApiKeyViaSecretStore()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (result, dto) = await service.CreateAsync(
            new CreateLlmSettingRequest("My OpenAI", "openai", "sk-plain", "gpt-4o", null, null),
            "alice");

        Assert.Equal(CreateLlmSettingResult.Created, result);
        Assert.NotNull(dto);
        Assert.Equal("OpenAI", dto!.Provider);
        Assert.Equal("alice", dto.OwnerUserId);

        // Secret store was called with the canonical key shape.
        var expectedKey = $"andy.issues.user.alice.llm.{dto.Id}.apiKey";
        Assert.True(_secretStore.Stored.ContainsKey(expectedKey));
        Assert.Equal("sk-plain", _secretStore.Stored[expectedKey]);

        // Column stores the reference key returned by StoreAsync, not plaintext.
        await using var verify = NewContext();
        var row = await verify.LlmSettings.FirstAsync(l => l.Id == dto.Id);
        Assert.Equal($"secret::{expectedKey}", row.ApiKey);
    }

    [Fact]
    public async Task Create_FirstSettingBecomesDefault()
    {
        await using var ctx = NewContext();
        var service = NewService(ctx);

        var (_, dto) = await service.CreateAsync(
            new CreateLlmSettingRequest("first", "openai", "k", "gpt-4o", null, null),
            "alice");

        Assert.True(dto!.IsDefault);
    }

    [Fact]
    public async Task Create_IsDefaultTrue_ClearsOtherDefaults()
    {
        await using (var ctx = NewContext())
        {
            var svc = NewService(ctx);
            await svc.CreateAsync(
                new CreateLlmSettingRequest("a", "openai", "k", "gpt-4o", null, null),
                "alice");
        }

        await using (var ctx = NewContext())
        {
            var svc = NewService(ctx);
            var (_, second) = await svc.CreateAsync(
                new CreateLlmSettingRequest("b", "anthropic", "k2", "claude-opus-4-6", null, true),
                "alice");
            Assert.True(second!.IsDefault);
        }

        await using var verify = NewContext();
        var defaults = await verify.LlmSettings.Where(l => l.IsDefault && l.OwnerUserId == "alice").ToListAsync();
        Assert.Single(defaults);
    }

    [Fact]
    public async Task Create_InvalidProvider_Rejected()
    {
        await using var ctx = NewContext();
        var (result, _) = await NewService(ctx).CreateAsync(
            new CreateLlmSettingRequest("x", "gemini", "k", "m", null, null),
            "alice");
        Assert.Equal(CreateLlmSettingResult.InvalidProvider, result);
    }

    [Fact]
    public async Task Create_InvalidBaseUrl_Rejected()
    {
        await using var ctx = NewContext();
        var (result, _) = await NewService(ctx).CreateAsync(
            new CreateLlmSettingRequest("x", "openai", "k", "m", "ftp://bad.example", null),
            "alice");
        Assert.Equal(CreateLlmSettingResult.InvalidBaseUrl, result);
    }

    [Fact]
    public async Task Create_BaseUrlAbsoluteHttps_Accepted()
    {
        await using var ctx = NewContext();
        var (result, dto) = await NewService(ctx).CreateAsync(
            new CreateLlmSettingRequest("x", "custom", "k", "m", "https://api.custom.local/v1", null),
            "alice");
        Assert.Equal(CreateLlmSettingResult.Created, result);
        Assert.Equal("https://api.custom.local/v1", dto!.BaseUrl);
    }

    // MARK: - List / Get ownership

    [Fact]
    public async Task List_ReturnsOnlyOwnerRows()
    {
        await using (var ctx = NewContext())
        {
            ctx.LlmSettings.AddRange(
                new LlmSetting { Id = Guid.NewGuid(), OwnerUserId = "alice", Name = "a", Provider = LlmProvider.OpenAI, ApiKey = "k", Model = "m" },
                new LlmSetting { Id = Guid.NewGuid(), OwnerUserId = "bob", Name = "b", Provider = LlmProvider.OpenAI, ApiKey = "k", Model = "m" });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        var list = await NewService(ctx2).ListAsync("alice");
        Assert.Single(list);
        Assert.Equal("alice", list[0].OwnerUserId);
    }

    [Fact]
    public async Task Get_OtherUsersRow_ReturnsNull()
    {
        var id = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.LlmSettings.Add(new LlmSetting
            {
                Id = id,
                OwnerUserId = "bob",
                Name = "b",
                Provider = LlmProvider.OpenAI,
                ApiKey = "k",
                Model = "m"
            });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = NewContext();
        Assert.Null(await NewService(ctx2).GetAsync(id, "alice"));
    }

    // MARK: - Update

    [Fact]
    public async Task Update_OnlySuppliedFields_AreChanged()
    {
        Guid id;
        await using (var ctx = NewContext())
        {
            var (_, dto) = await NewService(ctx).CreateAsync(
                new CreateLlmSettingRequest("orig", "openai", "orig-key", "gpt-4o", null, null),
                "alice");
            id = dto!.Id;
        }

        await using (var ctx = NewContext())
        {
            var (result, dto) = await NewService(ctx).UpdateAsync(
                id,
                new UpdateLlmSettingRequest(Name: "renamed", Provider: null, ApiKey: null, Model: null, BaseUrl: null, IsDefault: null),
                "alice");
            Assert.Equal(UpdateLlmSettingResult.Updated, result);
            Assert.Equal("renamed", dto!.Name);
            Assert.Equal("gpt-4o", dto.Model); // unchanged
        }
    }

    [Fact]
    public async Task Update_NullApiKey_KeepsExistingSecret()
    {
        Guid id;
        await using (var ctx = NewContext())
        {
            var (_, dto) = await NewService(ctx).CreateAsync(
                new CreateLlmSettingRequest("x", "openai", "first-key", "gpt-4o", null, null),
                "alice");
            id = dto!.Id;
        }
        var keyBefore = (await QueryRowAsync(id)).ApiKey;

        await using (var ctx = NewContext())
        {
            await NewService(ctx).UpdateAsync(
                id,
                new UpdateLlmSettingRequest(Name: "renamed", Provider: null, ApiKey: null, Model: null, BaseUrl: null, IsDefault: null),
                "alice");
        }

        Assert.Equal(keyBefore, (await QueryRowAsync(id)).ApiKey);
    }

    [Fact]
    public async Task Update_NewApiKey_RotatesSecretStoreEntry()
    {
        Guid id;
        await using (var ctx = NewContext())
        {
            var (_, dto) = await NewService(ctx).CreateAsync(
                new CreateLlmSettingRequest("x", "openai", "first-key", "gpt-4o", null, null),
                "alice");
            id = dto!.Id;
        }

        await using (var ctx = NewContext())
        {
            await NewService(ctx).UpdateAsync(
                id,
                new UpdateLlmSettingRequest(Name: null, Provider: null, ApiKey: "rotated-key", Model: null, BaseUrl: null, IsDefault: null),
                "alice");
        }

        var expectedKey = $"andy.issues.user.alice.llm.{id}.apiKey";
        Assert.Equal("rotated-key", _secretStore.Stored[expectedKey]);
    }

    [Fact]
    public async Task Update_OtherUsersRow_ReturnsNotFound()
    {
        Guid id;
        await using (var ctx = NewContext())
        {
            var (_, dto) = await NewService(ctx).CreateAsync(
                new CreateLlmSettingRequest("x", "openai", "k", "m", null, null),
                "bob");
            id = dto!.Id;
        }

        await using var ctx2 = NewContext();
        var (result, _) = await NewService(ctx2).UpdateAsync(
            id,
            new UpdateLlmSettingRequest("renamed", null, null, null, null, null),
            "alice");
        Assert.Equal(UpdateLlmSettingResult.NotFound, result);
    }

    [Fact]
    public async Task Update_InvalidProvider_Rejected()
    {
        Guid id;
        await using (var ctx = NewContext())
        {
            var (_, dto) = await NewService(ctx).CreateAsync(
                new CreateLlmSettingRequest("x", "openai", "k", "m", null, null),
                "alice");
            id = dto!.Id;
        }

        await using var ctx2 = NewContext();
        var (result, _) = await NewService(ctx2).UpdateAsync(
            id,
            new UpdateLlmSettingRequest(null, "mistral", null, null, null, null),
            "alice");
        Assert.Equal(UpdateLlmSettingResult.InvalidProvider, result);
    }

    // MARK: - Delete

    [Fact]
    public async Task Delete_OwnerRow_Succeeds()
    {
        Guid id;
        await using (var ctx = NewContext())
        {
            var (_, dto) = await NewService(ctx).CreateAsync(
                new CreateLlmSettingRequest("x", "openai", "k", "m", null, null),
                "alice");
            id = dto!.Id;
        }

        await using var ctx2 = NewContext();
        Assert.True(await NewService(ctx2).DeleteAsync(id, "alice"));
        await using var verify = NewContext();
        Assert.False(await verify.LlmSettings.AnyAsync(l => l.Id == id));
    }

    [Fact]
    public async Task Delete_OtherUsersRow_ReturnsFalse()
    {
        Guid id;
        await using (var ctx = NewContext())
        {
            var (_, dto) = await NewService(ctx).CreateAsync(
                new CreateLlmSettingRequest("x", "openai", "k", "m", null, null),
                "bob");
            id = dto!.Id;
        }

        await using var ctx2 = NewContext();
        Assert.False(await NewService(ctx2).DeleteAsync(id, "alice"));
    }

    // MARK: - SetDefault

    [Fact]
    public async Task SetDefault_PromotesRowAndClearsPreviousDefault()
    {
        Guid firstId, secondId;
        await using (var ctx = NewContext())
        {
            var (_, first) = await NewService(ctx).CreateAsync(
                new CreateLlmSettingRequest("a", "openai", "k", "m", null, null),
                "alice");
            firstId = first!.Id;
        }
        await using (var ctx = NewContext())
        {
            var (_, second) = await NewService(ctx).CreateAsync(
                new CreateLlmSettingRequest("b", "anthropic", "k", "m", null, null),
                "alice");
            secondId = second!.Id;
        }

        await using (var ctx = NewContext())
        {
            Assert.True(await NewService(ctx).SetDefaultAsync(secondId, "alice"));
        }

        await using var verify = NewContext();
        var rows = await verify.LlmSettings.Where(l => l.OwnerUserId == "alice").ToListAsync();
        Assert.False(rows.Single(r => r.Id == firstId).IsDefault);
        Assert.True(rows.Single(r => r.Id == secondId).IsDefault);
    }

    [Fact]
    public async Task SetDefault_OtherUsersRow_ReturnsFalse()
    {
        Guid id;
        await using (var ctx = NewContext())
        {
            var (_, dto) = await NewService(ctx).CreateAsync(
                new CreateLlmSettingRequest("x", "openai", "k", "m", null, null),
                "bob");
            id = dto!.Id;
        }

        await using var ctx2 = NewContext();
        Assert.False(await NewService(ctx2).SetDefaultAsync(id, "alice"));
    }

    private async Task<LlmSetting> QueryRowAsync(Guid id)
    {
        await using var ctx = NewContext();
        return await ctx.LlmSettings.AsNoTracking().FirstAsync(l => l.Id == id);
    }

    /// <summary>
    /// Like <see cref="StubSecretStore"/> but retains every key/value pair
    /// and returns a <c>secret::</c>-prefixed reference so the test can
    /// verify (a) the column holds a reference, (b) the canonical key
    /// was used, (c) rotations overwrite the stored plaintext.
    /// </summary>
    private sealed class RecordingSecretStore : ISecretStore
    {
        public Dictionary<string, string> Stored { get; } = new();

        public Task<string?> ResolveAsync(string? valueOrRef, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(valueOrRef))
                return Task.FromResult<string?>(null);
            if (valueOrRef.StartsWith("secret::", StringComparison.Ordinal))
                return Task.FromResult<string?>(Stored.GetValueOrDefault(valueOrRef["secret::".Length..]));
            return Task.FromResult<string?>(valueOrRef);
        }

        public Task<string> StoreAsync(string key, string value, CancellationToken ct = default)
        {
            Stored[key] = value;
            return Task.FromResult($"secret::{key}");
        }
    }
}
