using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.Issues.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryContentHash : Migration
    {
        // SP.7.1 (andy-issues#181 / conductor#1627) — stable sha256 hex
        // over the story's canonicalised title/body/labels/AC so
        // downstream services (andy-tasks, Conductor) can detect drift
        // after a re-import.
        //
        // The column is nullable: existing rows land with NULL and are
        // lazily backfilled on first read via the Application layer
        // mapping (`UserStory.ToDto` falls back to recomputing the hash
        // when ContentHash is null). The AppDbContext SaveChanges hook
        // populates the column for any subsequent UserStory write, so a
        // row is hashed at the latest on its next mutation.
        //
        // Postgres-only. The embedded SQLite path goes through
        // EnsureCreatedAsync() and picks the column up from the current
        // AppDbContext configuration rather than running migrations.
        //
        // Note: this migration intentionally does NOT re-seed
        // `backlog_sequences`. Those rows were created by the
        // AddBacklogDisplayIds migration (entity_type 0/1/2) and the
        // AddIssueDisplayIdAndGoalLinkage migration (entity_type 3) via
        // raw INSERT statements; the AppDbContext's `HasData` block is
        // only needed for the SQLite EnsureCreated path. The model
        // snapshot was previously out of sync with the runtime model on
        // this entity, and the snapshot will now reflect HasData for
        // consistency, but the migration body stays a no-op for those
        // rows because production Postgres data is already present.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "UserStories",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "UserStories");
        }
    }
}
