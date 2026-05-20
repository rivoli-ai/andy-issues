using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.Issues.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryRefinement : Migration
    {
        // SP.0.4 (andy-issues#180 / conductor#1632) — adds the refinement
        // output columns the triage agent populates when
        // POST /api/stories/{id}/refine completes:
        //
        //   • Priority / Complexity / Risk : enum strings (nullable)
        //   • SuggestedApproach            : text (nullable)
        //   • RefinedDescription           : text (nullable)
        //   • AcceptanceCriteriaList / Risks / TestPlan
        //                                  : JSON-encoded List<string>
        //   • RefineVersion                : int, default 0
        //   • RefinedAt / RefinedBy        : timestamp + actor (nullable)
        //   • StoryContentHashAtTriage     : 64-char hex hash captured
        //                                    from SP.7.1 ContentHash at
        //                                    refine time for drift /
        //                                    "Obsolete" derivation.
        //
        // Existing rows land with all columns at the appropriate "not
        // refined" defaults (RefineVersion=0, every nullable column
        // NULL, list columns empty-string which the JSON converter
        // decodes as `new List<string>()`).
        //
        // Postgres-only. The embedded SQLite path goes through
        // EnsureCreatedAsync() + SqliteSchemaBootstrapper, which picks
        // the new columns up from the live model automatically.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcceptanceCriteriaList",
                table: "UserStories",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Complexity",
                table: "UserStories",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "UserStories",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefineVersion",
                table: "UserStories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RefinedAt",
                table: "UserStories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefinedBy",
                table: "UserStories",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefinedDescription",
                table: "UserStories",
                type: "character varying(16384)",
                maxLength: 16384,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Risk",
                table: "UserStories",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Risks",
                table: "UserStories",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StoryContentHashAtTriage",
                table: "UserStories",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedApproach",
                table: "UserStories",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestPlan",
                table: "UserStories",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptanceCriteriaList",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "Complexity",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "RefineVersion",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "RefinedAt",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "RefinedBy",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "RefinedDescription",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "Risk",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "Risks",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "StoryContentHashAtTriage",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "SuggestedApproach",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "TestPlan",
                table: "UserStories");
        }
    }
}
