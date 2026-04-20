using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.Issues.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelsAndGitHubTypeToBacklog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GitHubType",
                table: "UserStories",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Labels",
                table: "UserStories",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GitHubType",
                table: "Features",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Labels",
                table: "Features",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GitHubType",
                table: "Epics",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Labels",
                table: "Epics",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GitHubType",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "Labels",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "GitHubType",
                table: "Features");

            migrationBuilder.DropColumn(
                name: "Labels",
                table: "Features");

            migrationBuilder.DropColumn(
                name: "GitHubType",
                table: "Epics");

            migrationBuilder.DropColumn(
                name: "Labels",
                table: "Epics");
        }
    }
}
