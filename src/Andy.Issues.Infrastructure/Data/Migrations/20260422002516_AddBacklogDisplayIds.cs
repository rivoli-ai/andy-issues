using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.Issues.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBacklogDisplayIds : Migration
    {
        // AH1 — short-id allocation for backlog entities.
        //
        // Schema order matters: add columns, create counter table,
        // backfill Seq for existing rows deterministically (order by
        // CreatedAt ASC, Id ASC), seed the counter rows to
        // max(Seq)+1 per type, THEN create the unique indexes. If
        // we created the unique index before backfill, every row's
        // default 0 would collide.
        //
        // Postgres-only. The embedded SQLite path goes through
        // EnsureCreatedAsync() and picks the schema up from the
        // current AppDbContext configuration rather than running
        // migrations (see docs/deployment.md).
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Seq",
                table: "UserStories",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Seq",
                table: "Features",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Seq",
                table: "Epics",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "backlog_sequences",
                columns: table => new
                {
                    entity_type = table.Column<int>(type: "integer", nullable: false),
                    next_seq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backlog_sequences", x => x.entity_type);
                });

            // Backfill: deterministic ordering on (CreatedAt, Id).
            // `Id` tiebreak guarantees stability when a fixture or
            // bulk import writes multiple rows with identical
            // timestamps — without it, the row_number would depend
            // on Postgres' internal row ordering.
            migrationBuilder.Sql(@"
                UPDATE ""Epics"" e
                SET ""Seq"" = s.rn
                FROM (
                    SELECT ""Id"", row_number() OVER (ORDER BY ""CreatedAt"" ASC, ""Id"" ASC) AS rn
                    FROM ""Epics""
                ) s
                WHERE e.""Id"" = s.""Id"";");

            migrationBuilder.Sql(@"
                UPDATE ""Features"" f
                SET ""Seq"" = s.rn
                FROM (
                    SELECT ""Id"", row_number() OVER (ORDER BY ""CreatedAt"" ASC, ""Id"" ASC) AS rn
                    FROM ""Features""
                ) s
                WHERE f.""Id"" = s.""Id"";");

            migrationBuilder.Sql(@"
                UPDATE ""UserStories"" u
                SET ""Seq"" = s.rn
                FROM (
                    SELECT ""Id"", row_number() OVER (ORDER BY ""CreatedAt"" ASC, ""Id"" ASC) AS rn
                    FROM ""UserStories""
                ) s
                WHERE u.""Id"" = s.""Id"";");

            // Seed the three counter rows. Each gets max(Seq)+1 so
            // the first post-migration allocation picks up where the
            // backfill left off. Empty tables start at 1.
            // entity_type: 0=Epic, 1=Feature, 2=Story — must match
            // BacklogEntityType.
            migrationBuilder.Sql(@"
                INSERT INTO backlog_sequences (entity_type, next_seq)
                VALUES (0, COALESCE((SELECT MAX(""Seq"") FROM ""Epics""), 0) + 1);");

            migrationBuilder.Sql(@"
                INSERT INTO backlog_sequences (entity_type, next_seq)
                VALUES (1, COALESCE((SELECT MAX(""Seq"") FROM ""Features""), 0) + 1);");

            migrationBuilder.Sql(@"
                INSERT INTO backlog_sequences (entity_type, next_seq)
                VALUES (2, COALESCE((SELECT MAX(""Seq"") FROM ""UserStories""), 0) + 1);");

            migrationBuilder.CreateIndex(
                name: "IX_UserStories_Seq",
                table: "UserStories",
                column: "Seq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Features_Seq",
                table: "Features",
                column: "Seq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Epics_Seq",
                table: "Epics",
                column: "Seq",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backlog_sequences");

            migrationBuilder.DropIndex(
                name: "IX_UserStories_Seq",
                table: "UserStories");

            migrationBuilder.DropIndex(
                name: "IX_Features_Seq",
                table: "Features");

            migrationBuilder.DropIndex(
                name: "IX_Epics_Seq",
                table: "Epics");

            migrationBuilder.DropColumn(
                name: "Seq",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "Seq",
                table: "Features");

            migrationBuilder.DropColumn(
                name: "Seq",
                table: "Epics");
        }
    }
}
