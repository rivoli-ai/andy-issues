// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Application.Requests;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.Enums;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Issues.Tests.Unit.Services;

public class BacklogAiServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BacklogAiServiceTests()
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

    // MARK: - BuildPrompt (pure)

    [Theory]
    [InlineData(BacklogAiService.BacklogItemType.Epic, "concise strategic goal")]
    [InlineData(BacklogAiService.BacklogItemType.Feature, "clear feature description")]
    [InlineData(BacklogAiService.BacklogItemType.Story, "As a [role], I want [capability]")]
    public void BuildPrompt_Description_IncludesItemTypeSpecificPhrasing(
        BacklogAiService.BacklogItemType itemType, string expectedSubstring)
    {
        var prompt = BacklogAiService.BuildPrompt(
            field: BacklogAiService.SuggestField.Description,
            itemType: itemType,
            title: "Login flow",
            currentContent: null);

        Assert.Contains(expectedSubstring, prompt);
        Assert.Contains("Login flow", prompt);
    }

    [Fact]
    public void BuildPrompt_AcceptanceCriteria_UsesGivenWhenThen()
    {
        var prompt = BacklogAiService.BuildPrompt(
            field: BacklogAiService.SuggestField.AcceptanceCriteria,
            itemType: BacklogAiService.BacklogItemType.Story,
            title: "OAuth2 login",
            currentContent: null);

        Assert.Contains("Given/When/Then", prompt);
        Assert.Contains("OAuth2 login", prompt);
    }

    [Fact]
    public void BuildPrompt_WithCurrentContent_SwitchesToRefineMode()
    {
        var prompt = BacklogAiService.BuildPrompt(
            field: BacklogAiService.SuggestField.Description,
            itemType: BacklogAiService.BacklogItemType.Story,
            title: "Search",
            currentContent: "A rough first draft describing the search feature.");

        Assert.Contains("Refine", prompt);
        Assert.Contains("A rough first draft describing the search feature.", prompt);
    }

    [Fact]
    public void BuildPrompt_EmptyCurrentContent_StaysInGenerateMode()
    {
        var bare = BacklogAiService.BuildPrompt(
            field: BacklogAiService.SuggestField.Description,
            itemType: BacklogAiService.BacklogItemType.Epic,
            title: "A",
            currentContent: null);
        var whitespace = BacklogAiService.BuildPrompt(
            field: BacklogAiService.SuggestField.Description,
            itemType: BacklogAiService.BacklogItemType.Epic,
            title: "A",
            currentContent: "   \n\t  ");

        Assert.DoesNotContain("Refine", bare);
        Assert.DoesNotContain("Refine", whitespace);
    }

    // MARK: - StripCodeFences

    [Fact]
    public void StripCodeFences_RemovesSurroundingFences()
    {
        const string input = "```\nreal content here\nmore lines\n```";
        Assert.Equal("real content here\nmore lines", BacklogAiService.StripCodeFences(input));
    }

    [Fact]
    public void StripCodeFences_LeavesUnfencedInputAlone()
    {
        const string input = "plain text with no fences";
        Assert.Equal(input, BacklogAiService.StripCodeFences(input));
    }

    // MARK: - SuggestContentAsync — end-to-end with HTTP stub

    [Fact]
    public async Task SuggestContentAsync_InvalidField_Returns400()
    {
        var svc = MakeService(out _);
        var (outcome, suggestion, error) = await svc.SuggestContentAsync(
            new SuggestContentRequest(
                Field: "hallucination",
                ItemType: "story",
                Title: "x",
                CurrentContent: null,
                RepositoryId: null),
            userId: "user-1");

        Assert.Equal(SuggestContentOutcome.InvalidField, outcome);
        Assert.Null(suggestion);
        Assert.Contains("Unknown field", error);
    }

    [Fact]
    public async Task SuggestContentAsync_AcceptanceCriteriaOnEpic_Returns400()
    {
        var svc = MakeService(out _);
        var (outcome, _, error) = await svc.SuggestContentAsync(
            new SuggestContentRequest(
                Field: "acceptanceCriteria",
                ItemType: "epic",
                Title: "Authentication",
                CurrentContent: null,
                RepositoryId: null),
            userId: "user-1");

        Assert.Equal(SuggestContentOutcome.InvalidField, outcome);
        Assert.Contains("only supported for stories", error);
    }

    [Fact]
    public async Task SuggestContentAsync_NoDefaultLlmAndNoRepo_Returns400()
    {
        var svc = MakeService(out _);
        var (outcome, _, error) = await svc.SuggestContentAsync(
            new SuggestContentRequest(
                Field: "description",
                ItemType: "story",
                Title: "Login",
                CurrentContent: null,
                RepositoryId: null),
            userId: "lonely-user");

        Assert.Equal(SuggestContentOutcome.NoLlmSetting, outcome);
        Assert.Contains("LLM setting", error);
    }

    [Fact]
    public async Task SuggestContentAsync_HappyPath_ReturnsTrimmedSuggestion()
    {
        using var ctx = new AppDbContext(_options);
        ctx.LlmSettings.Add(new LlmSetting
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "user-1",
            Name = "default",
            Provider = LlmProvider.OpenAI,
            ApiKey = "sk-stub",
            Model = "gpt-4o-mini",
            IsDefault = true
        });
        await ctx.SaveChangesAsync();

        var factory = StubHttpClientFactory(" \n  As a user, I want secure login, so that my account stays safe.\n ");
        var svc = new BacklogAiService(
            new AppDbContext(_options),
            new AlwaysOwnerGuard(),
            factory,
            NullLogger<BacklogAiService>.Instance);

        var (outcome, suggestion, error) = await svc.SuggestContentAsync(
            new SuggestContentRequest(
                Field: "description",
                ItemType: "story",
                Title: "Login",
                CurrentContent: null,
                RepositoryId: null),
            userId: "user-1");

        Assert.Equal(SuggestContentOutcome.Ok, outcome);
        Assert.Null(error);
        Assert.Equal(
            "As a user, I want secure login, so that my account stays safe.",
            suggestion);
    }

    [Fact]
    public async Task SuggestContentAsync_RepoMissing_Returns404()
    {
        var svc = MakeService(out _);
        var (outcome, _, _) = await svc.SuggestContentAsync(
            new SuggestContentRequest(
                Field: "description",
                ItemType: "story",
                Title: "X",
                CurrentContent: null,
                RepositoryId: Guid.NewGuid()),
            userId: "user-1");

        Assert.Equal(SuggestContentOutcome.RepositoryNotFound, outcome);
    }

    // MARK: - Helpers

    private BacklogAiService MakeService(out IHttpClientFactory factory)
    {
        factory = StubHttpClientFactory("fallback response");
        return new BacklogAiService(
            new AppDbContext(_options),
            new AlwaysOwnerGuard(),
            factory,
            NullLogger<BacklogAiService>.Instance);
    }

    private static IHttpClientFactory StubHttpClientFactory(string assistantContent)
    {
        var handler = new StubHandler(assistantContent);
        var client = new HttpClient(handler);
        return new SingleClientFactory(client);
    }

    private sealed class AlwaysOwnerGuard : IRepositoryAccessGuard
    {
        public Task<bool> CanViewAsync(Guid repositoryId, string userId, CancellationToken ct = default)
            => Task.FromResult(true);
        public Task<bool> IsOwnerAsync(Guid repositoryId, string userId, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _content;
        public StubHandler(string content) => _content = content;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    choices = new[]
                    {
                        new { message = new { role = "assistant", content = _content } }
                    }
                })
            };
            return Task.FromResult(response);
        }
    }
}
