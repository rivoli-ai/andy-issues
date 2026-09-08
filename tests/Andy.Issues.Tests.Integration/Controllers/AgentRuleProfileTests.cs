using System.Net;
using System.Net.Http.Json;
using Andy.Issues.Application.Dtos;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Andy.Issues.Tests.Integration.Controllers;

public class AgentRuleProfileTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient client = factory.CreateClient();
    private async Task<(Guid Repo, Guid Story, Guid Feature)> Seed(string owner = "dev-user", string? legacy = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new Repository { Id = Guid.NewGuid(), OwnerUserId = owner, Name = "profiles", CloneUrl = "https://example.com/repo", AgentRules = legacy };
        var epic = new Epic { Id = Guid.NewGuid(), Repository = repo, Title = "Epic" };
        var feature = new Feature { Id = Guid.NewGuid(), Epic = epic, Title = "Feature" };
        var story = new UserStory { Id = Guid.NewGuid(), Feature = feature, Title = "Story" };
        db.UserStories.Add(story); await db.SaveChangesAsync(); return (repo.Id, story.Id, feature.Id);
    }
    private async Task<AgentRuleProfileDto[]> Create(Guid repo, string name, string body, bool isDefault = false, int order = 0)
    {
        var response = await client.PostAsJsonAsync($"/api/repositories/{repo}/agent-rules", new AgentRuleWriteRequest(name, body, isDefault, order));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<AgentRuleProfileDto[]>())!;
    }
    [Fact]
    public async Task Selection_DefaultDeletion_AndSystemFallback()
    {
        var (repo, story, _) = await Seed();
        factory.FakeAndySettingsClient.Set("andy-issues:agent-rules:system-default", "system instructions");
        var effective = await client.GetFromJsonAsync<EffectiveAgentRulesDto>($"/api/stories/{story}/effective-agent-rules");
        Assert.Equal("system-default", effective!.Source); Assert.Equal("system instructions", effective.Rules);
        var first = (await Create(repo, "Default", "first body")).Single(); Assert.True(first.IsDefault);
        var second = (await Create(repo, "Review", "review body", order: 99)).Single(x => x.Name == "Review");
        var third = (await Create(repo, "Test", "test body", order: 5)).Single(x => x.Name == "Test");
        effective = await client.GetFromJsonAsync<EffectiveAgentRulesDto>($"/api/stories/{story}/effective-agent-rules");
        Assert.Equal(first.Id, effective!.AgentRuleId); Assert.Equal("repository-default", effective.Source);
        (await client.PutAsJsonAsync($"/api/stories/{story}/agent-rule", new StoryAgentRuleRequest(third.Id))).EnsureSuccessStatusCode();
        effective = await client.GetFromJsonAsync<EffectiveAgentRulesDto>($"/api/stories/{story}/effective-agent-rules");
        Assert.Equal("story", effective!.Source); Assert.Equal("test body", effective.Rules);
        (await client.DeleteAsync($"/api/repositories/{repo}/agent-rules/{third.Id}")).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope()) Assert.Null((await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserStories.FindAsync(story))!.AgentRuleId);
        (await client.DeleteAsync($"/api/repositories/{repo}/agent-rules/{first.Id}")).EnsureSuccessStatusCode();
        var profiles = await client.GetFromJsonAsync<AgentRuleProfileDto[]>($"/api/repositories/{repo}/agent-rules/profiles");
        Assert.Equal(second.Id, profiles!.Single(x => x.IsDefault).Id);
        var legacy = await client.GetFromJsonAsync<AgentRulesDto>($"/api/repositories/{repo}/agent-rules");
        Assert.Equal("review body", legacy!.Rules);
        (await client.DeleteAsync($"/api/repositories/{repo}/agent-rules/{second.Id}")).EnsureSuccessStatusCode();
        effective = await client.GetFromJsonAsync<EffectiveAgentRulesDto>($"/api/stories/{story}/effective-agent-rules");
        Assert.Equal("system-default", effective!.Source);
    }
    [Fact]
    public async Task LegacyPut_UpdatesDefaultWithoutReplacingOtherProfiles()
    {
        var (repo, _, _) = await Seed();
        var first = (await Create(repo, "First", "first")).Single();
        var second = (await Create(repo, "Second", "second", true)).Single(x => x.Name == "Second");
        (await client.PutAsJsonAsync($"/api/repositories/{repo}/agent-rules", new { rules = "legacy update" })).EnsureSuccessStatusCode();
        var profiles = await client.GetFromJsonAsync<AgentRuleProfileDto[]>($"/api/repositories/{repo}/agent-rules/profiles");
        Assert.Equal(2, profiles!.Length); Assert.Equal("first", profiles.Single(x => x.Id == first.Id).Body);
        Assert.Equal("legacy update", profiles.Single(x => x.Id == second.Id).Body);
        Assert.Single(profiles, x => x.IsDefault);
    }
    [Fact]
    public async Task InvalidReplaceAndForeignSelection_RejectWithoutChangingProfiles()
    {
        var (repo, story, feature) = await Seed(); var (other, _, _) = await Seed();
        var first = (await Create(repo, "First", "retained")).Single();
        var foreign = (await Create(other, "Foreign", "foreign")).Single();
        var response = await client.PostAsJsonAsync($"/api/repositories/{repo}/agent-rules/replace", new[] {
            new AgentRuleWriteRequest("Duplicate", "a", true, Id: first.Id), new AgentRuleWriteRequest("duplicate", "b") });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        response = await client.PostAsJsonAsync($"/api/repositories/{repo}/agent-rules/replace", new[] { new AgentRuleWriteRequest("Foreign", "body", Id: foreign.Id) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        response = await client.PutAsJsonAsync($"/api/stories/{story}/agent-rule", new StoryAgentRuleRequest(foreign.Id));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        response = await client.PostAsJsonAsync($"/api/features/{feature}/stories", new { title = "invalid selection", agentRuleId = foreign.Id });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var profiles = await client.GetFromJsonAsync<AgentRuleProfileDto[]>($"/api/repositories/{repo}/agent-rules/profiles");
        Assert.Equal(first, Assert.Single(profiles!));
    }
    [Fact]
    public async Task InaccessibleRepositoryAndStory_DoNotLeakProfiles()
    {
        var (repo, story, _) = await Seed("another-user");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/repositories/{repo}/agent-rules/profiles")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/repositories/{repo}/agent-rules", new AgentRuleWriteRequest("Denied", "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/stories/{story}/effective-agent-rules")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/stories/{story}/agent-rule", new StoryAgentRuleRequest(null))).StatusCode);
    }
    [Fact]
    public async Task ConcurrentProfileCreates_PreserveOneDefaultAndAllWrites()
    {
        var (repo, _, _) = await Seed();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Create(repo, $"Profile {i}", "body", true)));
        var profiles = await client.GetFromJsonAsync<AgentRuleProfileDto[]>($"/api/repositories/{repo}/agent-rules/profiles");
        Assert.Equal(8, profiles!.Length); Assert.Single(profiles, x => x.IsDefault);
    }

    [Fact]
    public async Task Backfill_IsIdempotentAndPreservesLegacyBody()
    {
        var (repo, _, _) = await Seed(legacy: "## Existing rules\nKeep me");
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await AgentRuleBackfill.BackfillAsync(db); await AgentRuleBackfill.BackfillAsync(db);
        var profile = Assert.Single(await db.AgentRules.Where(x => x.RepositoryId == repo).ToListAsync());
        Assert.True(profile.IsDefault); Assert.Equal("Default", profile.Name); Assert.Equal("## Existing rules\nKeep me", profile.Body);
    }
}
