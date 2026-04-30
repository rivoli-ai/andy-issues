// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class DraftBacklogGeneratorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public DraftBacklogGeneratorTests()
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

    // MARK: - ParseDraftBacklog

    [Fact]
    public void Parse_ValidJson_ReturnsDraftEpics()
    {
        var json = """
        {
          "epics": [
            {
              "title": "Infrastructure",
              "description": "Core infra work",
              "features": [
                {
                  "title": "CI/CD Pipeline",
                  "description": "Automated build",
                  "stories": [
                    {
                      "title": "Set up GitHub Actions",
                      "description": "As a dev, I want CI",
                      "acceptanceCriteria": "Given a push, builds run",
                      "storyPoints": 3
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var result = DraftBacklogGenerator.ParseDraftBacklog(json);

        Assert.Single(result);
        Assert.Equal("Infrastructure", result[0].Title);
        Assert.Equal("Core infra work", result[0].Description);
        Assert.Single(result[0].Features);
        Assert.Equal("CI/CD Pipeline", result[0].Features[0].Title);
        Assert.Single(result[0].Features[0].Stories);
        Assert.Equal("Set up GitHub Actions", result[0].Features[0].Stories[0].Title);
        Assert.Equal(3, result[0].Features[0].Stories[0].StoryPoints);
    }

    [Fact]
    public void Parse_EmptyEpicsArray_ReturnsEmptyList()
    {
        var json = """{ "epics": [] }""";
        var result = DraftBacklogGenerator.ParseDraftBacklog(json);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_MissingEpicsKey_Throws()
    {
        var json = """{ "data": [] }""";
        Assert.Throws<System.Text.Json.JsonException>(() =>
            DraftBacklogGenerator.ParseDraftBacklog(json));
    }

    [Fact]
    public void Parse_OptionalFieldsMissing_DefaultsGracefully()
    {
        var json = """
        {
          "epics": [
            {
              "title": "E1",
              "features": [
                {
                  "title": "F1",
                  "stories": [
                    { "title": "S1" }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var result = DraftBacklogGenerator.ParseDraftBacklog(json);
        Assert.Null(result[0].Description);
        Assert.Null(result[0].Features[0].Stories[0].AcceptanceCriteria);
        Assert.Null(result[0].Features[0].Stories[0].StoryPoints);
    }

    // MARK: - GenerateAsync guard checks

    [Fact]
    public async Task Generate_RepoNotFound_ReturnsNotFound()
    {
        await using var ctx = NewContext();
        var gen = CreateGenerator(ctx);

        var result = await gen.GenerateAsync(Guid.NewGuid(), "alice");

        Assert.Equal(DraftBacklogOutcome.RepositoryNotFound, result.Outcome);
    }

    [Fact]
    public async Task Generate_NonOwner_ReturnsNotOwner()
    {
        var repoId = await SeedRepoWithLlmAsync();
        await using var ctx = NewContext();
        var gen = CreateGenerator(ctx);

        var result = await gen.GenerateAsync(repoId, "mallory");

        Assert.Equal(DraftBacklogOutcome.NotOwner, result.Outcome);
    }

    [Fact]
    public async Task Generate_NoLlmSetting_ReturnsNoLlmSetting()
    {
        var repoId = await SeedRepoWithoutLlmAsync();
        await using var ctx = NewContext();
        var gen = CreateGenerator(ctx);

        var result = await gen.GenerateAsync(repoId, "alice");

        Assert.Equal(DraftBacklogOutcome.NoLlmSetting, result.Outcome);
    }

    [Fact]
    public async Task Generate_CodeIndexNotReady_ReturnsCodeIndexNotReady()
    {
        var repoId = await SeedRepoWithLlmAsync();
        var codeIndex = new StubCodeIndexClient();
        codeIndex.RegistrationResult = new CodeIndexRegistrationResult(
            CodeIndexRegistrationOutcome.ServiceUnavailable, null, "down");

        await using var ctx = NewContext();
        var gen = CreateGenerator(ctx, codeIndex: new NotReadyCodeIndexClient());

        var result = await gen.GenerateAsync(repoId, "alice");

        Assert.Equal(DraftBacklogOutcome.CodeIndexNotReady, result.Outcome);
    }

    // ── #103 — generation tracking ──────────────────────────────────

    [Fact]
    public async Task Generate_CodeIndexNotReady_RecordsFailedPhase_AndReturnsGenerationId()
    {
        var repoId = await SeedRepoWithLlmAsync();
        await using var ctx = NewContext();
        var tracker = new BacklogGenerationTracker(ctx, null);
        var gen = CreateGenerator(ctx, codeIndex: new NotReadyCodeIndexClient(), tracker: tracker);

        var result = await gen.GenerateAsync(repoId, "alice");

        Assert.Equal(DraftBacklogOutcome.CodeIndexNotReady, result.Outcome);
        Assert.NotNull(result.GenerationId);

        await using var verify = NewContext();
        var row = await verify.BacklogGenerations.SingleAsync(g => g.Id == result.GenerationId);
        Assert.Equal(BacklogGenerationPhase.Failed, row.Phase);
        Assert.NotNull(row.CompletedAt);
    }

    [Fact]
    public async Task Generate_NotOwner_DoesNotCreateGenerationRow()
    {
        var repoId = await SeedRepoWithLlmAsync();
        await using var ctx = NewContext();
        var tracker = new BacklogGenerationTracker(ctx, null);
        var gen = CreateGenerator(ctx, tracker: tracker);

        var result = await gen.GenerateAsync(repoId, "stranger");

        Assert.Equal(DraftBacklogOutcome.NotOwner, result.Outcome);
        Assert.Null(result.GenerationId);

        await using var verify = NewContext();
        Assert.Equal(0, await verify.BacklogGenerations.CountAsync());
    }

    // Helpers

    private DraftBacklogGenerator CreateGenerator(
        AppDbContext ctx,
        ICodeIndexClient? codeIndex = null,
        IBacklogGenerationTracker? tracker = null)
    {
        var guard = new RepositoryAccessGuard(ctx);
        var ci = codeIndex ?? new ReadyCodeIndexClient();
        // We use a null HTTP factory since tests that reach the LLM call
        // would need a full mock HTTP pipeline — those tests are covered
        // by integration tests. The guard/parse tests here never reach it.
        return new DraftBacklogGenerator(ctx, guard, ci, null!,
            new BacklogSequenceAllocator(ctx),
            NullLogger<DraftBacklogGenerator>.Instance,
            tracker);
    }

    private async Task<Guid> SeedRepoWithLlmAsync()
    {
        await using var ctx = NewContext();
        var llm = new LlmSetting
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "test-llm",
            Provider = LlmProvider.OpenAI,
            ApiKey = "sk-test",
            Model = "gpt-4o"
        };
        ctx.LlmSettings.Add(llm);
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "test-repo",
            CloneUrl = "https://github.com/test/repo.git",
            LlmSettingId = llm.Id
        };
        ctx.Repositories.Add(repo);
        await ctx.SaveChangesAsync();
        return repo.Id;
    }

    private async Task<Guid> SeedRepoWithoutLlmAsync()
    {
        await using var ctx = NewContext();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "alice",
            Name = "test-repo",
            CloneUrl = "https://github.com/test/repo.git"
        };
        ctx.Repositories.Add(repo);
        await ctx.SaveChangesAsync();
        return repo.Id;
    }

    private sealed class ReadyCodeIndexClient : ICodeIndexClient
    {
        public Task<CodeIndexRegistrationResult> RegisterAsync(string cloneUrl, string defaultBranch, CancellationToken ct) =>
            Task.FromResult(new CodeIndexRegistrationResult(CodeIndexRegistrationOutcome.Registered, "idx", null));
        public Task<CodeIndexStatusResult> GetStatusAsync(string cloneUrl, CancellationToken ct) =>
            Task.FromResult(new CodeIndexStatusResult(CodeIndexQueryOutcome.Ok, "Indexed", "3 modules, 42 files", null));
        public Task<bool> DeregisterAsync(string cloneUrl, CancellationToken ct) =>
            Task.FromResult(true);
    }

    private sealed class NotReadyCodeIndexClient : ICodeIndexClient
    {
        public Task<CodeIndexRegistrationResult> RegisterAsync(string cloneUrl, string defaultBranch, CancellationToken ct) =>
            Task.FromResult(new CodeIndexRegistrationResult(CodeIndexRegistrationOutcome.ServiceUnavailable, null, "down"));
        public Task<CodeIndexStatusResult> GetStatusAsync(string cloneUrl, CancellationToken ct) =>
            Task.FromResult(new CodeIndexStatusResult(CodeIndexQueryOutcome.ServiceUnavailable, null, null, "not ready"));
        public Task<bool> DeregisterAsync(string cloneUrl, CancellationToken ct) =>
            Task.FromResult(false);
    }
}
