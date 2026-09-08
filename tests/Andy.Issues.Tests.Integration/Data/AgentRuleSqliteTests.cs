using Andy.Issues.Application.Dtos;
using Andy.Issues.Application.Interfaces;
using Andy.Issues.Domain.Entities;
using Andy.Issues.Infrastructure.Data;
using Andy.Issues.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Andy.Issues.Tests.Integration.Fakes;
using Xunit;
namespace Andy.Issues.Tests.Integration.Data;

public class AgentRuleSqliteTests
{
    [Fact]
    public async Task AtomicNameAndDefaultSwap_AndDeleteSetNull_WorkWithRealConstraints()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var repo = new Repository { Id = Guid.NewGuid(), OwnerUserId = "owner", Name = "rules", CloneUrl = "https://example.com/repo" };
        db.Repositories.Add(repo); await db.SaveChangesAsync();
        var svc = new AgentRuleProfiles(db, new RepositoryAccessGuard(db), new AuditLogService(db), new FakeAndySettingsClient(), new NullBoardNotifier());
        var first = (await svc.WriteAsync(repo.Id, "owner", "create", new("First", "one")))!.Single();
        var second = (await svc.WriteAsync(repo.Id, "owner", "create", new("Second", "two")))!.Single(x => x.Name == "Second");
        var swapped = await svc.WriteAsync(repo.Id, "owner", "replace", replacement: [
            new("Second", "one", false, 1, first.Id), new("First", "two", true, 2, second.Id)]);
        Assert.Equal(second.Id, swapped!.Single(x => x.IsDefault).Id);
        Assert.Equal("Second", swapped!.Single(x => x.Id == first.Id).Name);
        await Assert.ThrowsAsync<AgentRuleValidationException>(() => svc.WriteAsync(repo.Id, "owner", "replace", replacement: [
            new("A", "one", true, Id: first.Id), new("B", "two", true, Id: second.Id)]));
        db.ChangeTracker.Clear();
        Assert.Equal(second.Id, (await db.AgentRules.SingleAsync(x => x.IsDefault)).Id);
        var epic = new Epic { Id = Guid.NewGuid(), RepositoryId = repo.Id, Title = "Epic" };
        var feature = new Feature { Id = Guid.NewGuid(), Epic = epic, Title = "Feature" };
        var story = new UserStory { Id = Guid.NewGuid(), Feature = feature, Title = "Story", AgentRuleId = second.Id };
        db.UserStories.Add(story); await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var rule = await db.AgentRules.FindAsync(second.Id); db.AgentRules.Remove(rule!); await db.SaveChangesAsync();
        Assert.Null((await db.UserStories.SingleAsync()).AgentRuleId);
    }
}
