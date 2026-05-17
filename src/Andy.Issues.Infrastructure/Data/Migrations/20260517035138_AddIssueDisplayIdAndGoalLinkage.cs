using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.Issues.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueDisplayIdAndGoalLinkage : Migration
    {
        // AH6 (rivoli-ai/conductor#713) — ISSUE-N short display id on
        // the Issue entity, plus the GoalDisplayId reverse-linkage
        // column written by the GoalLinkageConsumer when andy-tasks
        // emits the matching SourceIssueDisplayId on goal.created.
        //
        // Schema order matters: add columns, backfill Seq for existing
        // rows deterministically (ORDER BY CreatedAt ASC, Id ASC),
        // seed the backlog_sequences row for entity_type=3 (Issue) to
        // max(Seq)+1, THEN create the unique index. If we created the
        // unique index before backfill, every row's default 0 would
        // collide.
        //
        // Postgres-only. The embedded SQLite path goes through
        // EnsureCreatedAsync() and picks the schema up from the
        // current AppDbContext configuration rather than running
        // migrations.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoalDisplayId",
                table: "Issues",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Seq",
                table: "Issues",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Backfill: deterministic ordering on (CreatedAt, Id) so
            // re-runs on identical fixtures produce identical seqs.
            // Mirrors the AH1 AddBacklogDisplayIds migration pattern.
            migrationBuilder.Sql(@"
                UPDATE ""Issues"" i
                SET ""Seq"" = s.rn
                FROM (
                    SELECT ""Id"", row_number() OVER (ORDER BY ""CreatedAt"" ASC, ""Id"" ASC) AS rn
                    FROM ""Issues""
                ) s
                WHERE i.""Id"" = s.""Id"";");

            // Seed the new backlog_sequences row for Issue (entity_type=3).
            // Picks up at max(Seq)+1 so the first post-migration
            // allocation continues the sequence. Empty Issues tables
            // start at 1.
            migrationBuilder.Sql(@"
                INSERT INTO backlog_sequences (entity_type, next_seq)
                VALUES (3, COALESCE((SELECT MAX(""Seq"") FROM ""Issues""), 0) + 1);");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_GoalDisplayId",
                table: "Issues",
                column: "GoalDisplayId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Seq",
                table: "Issues",
                column: "Seq",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Issues_GoalDisplayId",
                table: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_Issues_Seq",
                table: "Issues");

            // Remove the seeded counter row for Issue.
            migrationBuilder.Sql(@"
                DELETE FROM backlog_sequences WHERE entity_type = 3;");

            migrationBuilder.DropColumn(
                name: "GoalDisplayId",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "Seq",
                table: "Issues");
        }
    }
}
