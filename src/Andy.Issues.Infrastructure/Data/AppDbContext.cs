// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Issues.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Andy.Issues.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // SQLite has no native DateTimeOffset type and cannot ORDER BY one.
        // Store as BIGINT via the built-in binary converter so comparisons and
        // ordering are valid. Postgres keeps its native timestamptz mapping.
        if (Database.IsSqlite())
        {
            configurationBuilder.Properties<DateTimeOffset>()
                .HaveConversion<DateTimeOffsetToBinaryConverter>();
            configurationBuilder.Properties<DateTimeOffset?>()
                .HaveConversion<DateTimeOffsetToBinaryConverter>();
        }
    }

    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<RepositoryShare> RepositoryShares => Set<RepositoryShare>();
    public DbSet<Epic> Epics => Set<Epic>();
    public DbSet<Feature> Features => Set<Feature>();
    public DbSet<UserStory> UserStories => Set<UserStory>();
    public DbSet<Issue> Issues => Set<Issue>();
    public DbSet<TriageOutputRevision> TriageOutputRevisions => Set<TriageOutputRevision>();
    public DbSet<IssueAttachment> IssueAttachments => Set<IssueAttachment>();
    public DbSet<BacklogGeneration> BacklogGenerations => Set<BacklogGeneration>();
    public DbSet<LinkedProvider> LinkedProviders => Set<LinkedProvider>();
    public DbSet<McpServerConfig> McpServerConfigs => Set<McpServerConfig>();
    public DbSet<ArtifactFeedConfig> ArtifactFeedConfigs => Set<ArtifactFeedConfig>();
    public DbSet<Sandbox> Sandboxes => Set<Sandbox>();
    public DbSet<LlmSetting> LlmSettings => Set<LlmSetting>();
    public DbSet<UserDirectoryEntry> UserDirectory => Set<UserDirectoryEntry>();
    public DbSet<OutboxEntry> Outbox => Set<OutboxEntry>();
    public DbSet<BacklogSequence> BacklogSequences => Set<BacklogSequence>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    /// <summary>
    /// Value converter + comparer for the <c>Labels</c> property on
    /// Epic / Feature / UserStory. Stores the list as a JSON string so
    /// it round-trips cleanly on both SQLite and Postgres without
    /// needing a join table. See conductor#670 Bug 2.
    /// </summary>
    private static readonly ValueConverter<List<string>, string> LabelsConverter = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => string.IsNullOrWhiteSpace(v)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
    );

    private static readonly Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>> LabelsComparer = new(
        (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
        v => v == null ? 0 : v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        v => v == null ? new List<string>() : v.ToList()
    );

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Repository>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OwnerUserId).IsRequired().HasMaxLength(256);
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
            e.Property(x => x.Description).HasMaxLength(2048);
            e.Property(x => x.CloneUrl).IsRequired().HasMaxLength(1024);
            e.Property(x => x.DefaultBranch).IsRequired().HasMaxLength(256);
            e.Property(x => x.ExternalId).HasMaxLength(256);
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.CodeIndexStatus).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.AzureClientId).HasMaxLength(256);
            e.Property(x => x.AzureClientSecret).HasMaxLength(1024);
            e.Property(x => x.AzureTenantId).HasMaxLength(256);
            e.Property(x => x.AzureSubscriptionId).HasMaxLength(256);
            e.Property(x => x.AzureOrganization).HasMaxLength(256);
            e.Property(x => x.AzureProject).HasMaxLength(256);
            e.Property(x => x.AzurePat).HasMaxLength(1024);
            e.Property(x => x.AgentRules).HasMaxLength(65536);
            e.HasIndex(x => x.OwnerUserId);
            e.HasOne(x => x.LlmSetting)
                .WithMany()
                .HasForeignKey(x => x.LlmSettingId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RepositoryShare>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SharedWithUserId).IsRequired().HasMaxLength(256);
            e.Property(x => x.GrantedByUserId).IsRequired().HasMaxLength(256);
            e.HasIndex(x => new { x.RepositoryId, x.SharedWithUserId }).IsUnique();
            e.HasOne(x => x.Repository)
                .WithMany(r => r.Shares)
                .HasForeignKey(x => x.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Epic>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Seq).IsRequired();
            e.HasIndex(x => x.Seq).IsUnique();
            e.Property(x => x.Title).IsRequired().HasMaxLength(512);
            e.Property(x => x.Description).HasMaxLength(4096);
            e.Property(x => x.ExternalId).HasMaxLength(256);
            e.Property(x => x.GitHubType).HasMaxLength(64);
            e.Property(x => x.Labels).HasConversion(LabelsConverter).Metadata.SetValueComparer(LabelsComparer);
            e.Ignore(x => x.DisplayId);
            e.HasOne(x => x.Repository)
                .WithMany(r => r.Epics)
                .HasForeignKey(x => x.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Feature>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Seq).IsRequired();
            e.HasIndex(x => x.Seq).IsUnique();
            e.Property(x => x.Title).IsRequired().HasMaxLength(512);
            e.Property(x => x.Description).HasMaxLength(4096);
            e.Property(x => x.ExternalId).HasMaxLength(256);
            e.Property(x => x.GitHubType).HasMaxLength(64);
            e.Property(x => x.Labels).HasConversion(LabelsConverter).Metadata.SetValueComparer(LabelsComparer);
            e.Ignore(x => x.DisplayId);
            e.HasOne(x => x.Epic)
                .WithMany(r => r.Features)
                .HasForeignKey(x => x.EpicId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserStory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Seq).IsRequired();
            e.HasIndex(x => x.Seq).IsUnique();
            e.Property(x => x.Title).IsRequired().HasMaxLength(512);
            e.Property(x => x.Description).HasMaxLength(4096);
            e.Property(x => x.AcceptanceCriteria).HasMaxLength(8192);
            e.Property(x => x.PullRequestUrl).HasMaxLength(1024);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ExternalId).HasMaxLength(256);
            e.Property(x => x.GitHubType).HasMaxLength(64);
            e.Property(x => x.Labels).HasConversion(LabelsConverter).Metadata.SetValueComparer(LabelsComparer);
            e.Ignore(x => x.DisplayId);
            e.HasIndex(x => x.AzureDevOpsWorkItemId);
            e.HasOne(x => x.Feature)
                .WithMany(f => f.Stories)
                .HasForeignKey(x => x.FeatureId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Issue>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OwnerUserId).IsRequired().HasMaxLength(256);
            e.Property(x => x.Title).IsRequired().HasMaxLength(512);
            e.Property(x => x.Body).HasMaxLength(8192);
            e.Property(x => x.TriageState).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.TriagedBy).HasMaxLength(256);
            e.HasIndex(x => x.OwnerUserId);
            e.HasIndex(x => x.TriageState);

            // Z3 — TriageOutput is a domain value, persisted as JSON
            // text (portable across SQLite + Postgres). The whole record
            // round-trips through System.Text.Json with the EventJson
            // snake_case options used by the outbox so both surfaces
            // agree on the wire shape.
            e.Property(x => x.TriageOutput)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, Andy.Issues.Application.Messaging.EventJson.Options),
                    v => string.IsNullOrEmpty(v) ? null : JsonSerializer.Deserialize<Andy.Issues.Domain.ValueTypes.TriageOutput>(v, Andy.Issues.Application.Messaging.EventJson.Options))
                .HasColumnName("TriageOutputJson");
        });

        // #103 — durable record of a draft-backlog generation run.
        // Indexed on RepositoryId + UpdatedAt so the GET /generations
        // late-join path is a fast point lookup; cascade with the
        // owning Repository so old runs disappear when the repo is
        // deleted.
        modelBuilder.Entity<BacklogGeneration>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).IsRequired().HasMaxLength(256);
            e.Property(x => x.Phase).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Detail).HasMaxLength(2048);
            e.HasIndex(x => new { x.RepositoryId, x.UpdatedAt });
            e.HasOne<Repository>()
                .WithMany()
                .HasForeignKey(x => x.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Z8 — input-resource attachments. Composite key prevents
        // duplicate pins of the same DocumentLink; cascade delete with
        // the Issue so the table never carries orphan rows.
        modelBuilder.Entity<IssueAttachment>(e =>
        {
            e.HasKey(x => new { x.IssueId, x.LinkId });
            e.Property(x => x.CreatedBy).IsRequired().HasMaxLength(256);
            e.HasIndex(x => x.IssueId);
            e.HasOne<Issue>()
                .WithMany()
                .HasForeignKey(x => x.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Z5 — versioned audit table for triage output. Cascade delete
        // with the parent Issue: revisions don't outlive their issue.
        modelBuilder.Entity<TriageOutputRevision>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.IssueId).IsRequired();
            e.Property(x => x.Author).IsRequired().HasMaxLength(256);
            e.Property(x => x.AuthorKind).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.DiffSummary).HasMaxLength(2048);
            e.Property(x => x.TriageOutput)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, Andy.Issues.Application.Messaging.EventJson.Options),
                    v => JsonSerializer.Deserialize<Andy.Issues.Domain.ValueTypes.TriageOutput>(v, Andy.Issues.Application.Messaging.EventJson.Options)!)
                .HasColumnName("TriageOutputJson")
                .IsRequired();
            // Composite index for the GET /revisions hot query:
            //   WHERE IssueId = ? ORDER BY CreatedAt DESC
            e.HasIndex(x => new { x.IssueId, x.CreatedAt });
            e.HasOne<Issue>()
                .WithMany()
                .HasForeignKey(x => x.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Counter row backing the per-type short-id sequences. Seeded
        // on first migration with NextSeq = max(Seq)+1 from the
        // corresponding entity table, so backfilled rows and new
        // allocations share a monotonic line.
        modelBuilder.Entity<BacklogSequence>(e =>
        {
            e.ToTable("backlog_sequences");
            e.HasKey(x => x.EntityType);
            e.Property(x => x.EntityType).HasColumnName("entity_type");
            e.Property(x => x.NextSeq).HasColumnName("next_seq").IsRequired();
        });

        modelBuilder.Entity<LinkedProvider>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OwnerUserId).IsRequired().HasMaxLength(256);
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.AccessToken).IsRequired().HasMaxLength(2048);
            e.Property(x => x.RefreshToken).HasMaxLength(2048);
            e.Property(x => x.AccountLogin).HasMaxLength(256);
            e.HasIndex(x => new { x.OwnerUserId, x.Provider }).IsUnique();
        });

        modelBuilder.Entity<McpServerConfig>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
            e.Property(x => x.Description).HasMaxLength(1024);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.OwnerUserId).HasMaxLength(256);
            e.Property(x => x.Command).HasMaxLength(1024);
            e.Property(x => x.Url).HasMaxLength(1024);
            e.HasIndex(x => new { x.OwnerUserId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<ArtifactFeedConfig>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
            e.Property(x => x.Organization).IsRequired().HasMaxLength(256);
            e.Property(x => x.FeedName).IsRequired().HasMaxLength(256);
            e.Property(x => x.Project).HasMaxLength(256);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<Sandbox>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ContainerId).IsRequired().HasMaxLength(128);
            e.Property(x => x.OwnerUserId).IsRequired().HasMaxLength(256);
            e.Property(x => x.Branch).IsRequired().HasMaxLength(256);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.IdeEndpoint).HasMaxLength(1024);
            e.Property(x => x.VncEndpoint).HasMaxLength(1024);
            e.HasIndex(x => x.ContainerId).IsUnique();
            e.HasIndex(x => x.OwnerUserId);
            e.HasOne(x => x.Repository)
                .WithMany()
                .HasForeignKey(x => x.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LlmSetting>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OwnerUserId).IsRequired().HasMaxLength(256);
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ApiKey).HasMaxLength(2048);
            e.Property(x => x.Model).IsRequired().HasMaxLength(256);
            e.Property(x => x.BaseUrl).HasMaxLength(1024);
        });

        modelBuilder.Entity<UserDirectoryEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).IsRequired().HasMaxLength(256);
            e.Property(x => x.Email).IsRequired().HasMaxLength(256);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<OutboxEntry>(e =>
        {
            e.ToTable("Outbox");
            e.HasKey(x => x.Id);
            e.Property(x => x.Subject).IsRequired().HasMaxLength(256);
            e.Property(x => x.PayloadType).HasMaxLength(256);
            e.Property(x => x.PayloadJson).IsRequired();
            e.Property(x => x.LastError).HasMaxLength(2000);

            // Composite index over the dispatcher's hot query:
            //   WHERE PublishedAt IS NULL ORDER BY CreatedAt
            // Plain (non-filtered) so the same DDL works on Postgres and SQLite.
            e.HasIndex(x => new { x.PublishedAt, x.CreatedAt });
            e.HasIndex(x => x.CorrelationId);
        });

        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("AuditLog");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).IsRequired().HasMaxLength(256);
            e.Property(x => x.Action).IsRequired().HasMaxLength(128);
            e.Property(x => x.ResourceType).IsRequired().HasMaxLength(64);
            e.Property(x => x.ResourceId).IsRequired().HasMaxLength(256);
            e.Property(x => x.Details).HasMaxLength(2048);
            // Hot query is "show recent activity for resource X":
            //   WHERE ResourceType = ? AND ResourceId = ? ORDER BY CreatedAt DESC
            e.HasIndex(x => new { x.ResourceType, x.ResourceId, x.CreatedAt });
            e.HasIndex(x => x.UserId);
        });
    }
}
