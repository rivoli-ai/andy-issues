// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

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
    public DbSet<LinkedProvider> LinkedProviders => Set<LinkedProvider>();
    public DbSet<McpServerConfig> McpServerConfigs => Set<McpServerConfig>();
    public DbSet<ArtifactFeedConfig> ArtifactFeedConfigs => Set<ArtifactFeedConfig>();
    public DbSet<Sandbox> Sandboxes => Set<Sandbox>();
    public DbSet<LlmSetting> LlmSettings => Set<LlmSetting>();
    public DbSet<UserDirectoryEntry> UserDirectory => Set<UserDirectoryEntry>();
    public DbSet<OutboxEntry> Outbox => Set<OutboxEntry>();

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
            e.Property(x => x.Title).IsRequired().HasMaxLength(512);
            e.Property(x => x.Description).HasMaxLength(4096);
            e.Property(x => x.ExternalId).HasMaxLength(256);
            e.HasOne(x => x.Repository)
                .WithMany(r => r.Epics)
                .HasForeignKey(x => x.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Feature>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired().HasMaxLength(512);
            e.Property(x => x.Description).HasMaxLength(4096);
            e.Property(x => x.ExternalId).HasMaxLength(256);
            e.HasOne(x => x.Epic)
                .WithMany(r => r.Features)
                .HasForeignKey(x => x.EpicId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserStory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired().HasMaxLength(512);
            e.Property(x => x.Description).HasMaxLength(4096);
            e.Property(x => x.AcceptanceCriteria).HasMaxLength(8192);
            e.Property(x => x.PullRequestUrl).HasMaxLength(1024);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ExternalId).HasMaxLength(256);
            e.HasIndex(x => x.AzureDevOpsWorkItemId);
            e.HasOne(x => x.Feature)
                .WithMany(f => f.Stories)
                .HasForeignKey(x => x.FeatureId)
                .OnDelete(DeleteBehavior.Cascade);
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
    }
}
