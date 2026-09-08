using Andy.Issues.Domain.Entities;
using Andy.Issues.Domain.ValueTypes;
using Andy.Issues.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace Andy.Issues.Tests.Integration.Data;

public class TriageAuditSqliteTests
{
    [Fact]
    public async Task ReferencesRoundTrip_AndStaleRunWriteFailsConcurrencyCheck()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using var db = new AppDbContext(options); await db.Database.EnsureCreatedAsync();
        var input = new DocsRef(Guid.NewGuid(), Guid.NewGuid()); var output = new DocsRef(Guid.NewGuid(), Guid.NewGuid());
        var issue = new Issue { Id = Guid.NewGuid(), OwnerUserId = "owner", Title = "Audit", TriageRunId = Guid.NewGuid(), TriageInputDocsRefs = [input], TriageOutputDocRef = output };
        issue.StartTriage(); db.Issues.Add(issue); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var loaded = await db.Issues.SingleAsync(); Assert.Equal(input, Assert.Single(loaded.TriageInputDocsRefs)); Assert.Equal(output, loaded.TriageOutputDocRef);
        loaded.TriageInputDocsRefs.Add(new DocsRef(Guid.NewGuid(), Guid.NewGuid())); await db.SaveChangesAsync();
        using var other = new AppDbContext(options); var current = await other.Issues.SingleAsync();
        Assert.Equal(2, current.TriageInputDocsRefs.Count);
        current.TriageRunId = Guid.NewGuid(); await other.SaveChangesAsync();
        loaded.CompleteTriage("owner");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }
}
