using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.Issues.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class NamedAgentRuleProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgentRuleId",
                table: "UserStories",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NameKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Body = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRules_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserStories_AgentRuleId",
                table: "UserStories",
                column: "AgentRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRules_RepositoryId",
                table: "AgentRules",
                column: "RepositoryId",
                unique: true,
                filter: "\"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRules_RepositoryId_NameKey",
                table: "AgentRules",
                columns: new[] { "RepositoryId", "NameKey" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO "AgentRules" ("Id", "RepositoryId", "Name", "NameKey", "Body", "IsDefault", "SortOrder", "CreatedAt")
                SELECT "Id", "Id", 'Default', 'DEFAULT', "AgentRules", true, 0, CURRENT_TIMESTAMP
                FROM "Repositories" WHERE "AgentRules" IS NOT NULL AND "AgentRules" <> '';
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_UserStories_AgentRules_AgentRuleId",
                table: "UserStories",
                column: "AgentRuleId",
                principalTable: "AgentRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserStories_AgentRules_AgentRuleId",
                table: "UserStories");

            migrationBuilder.DropTable(
                name: "AgentRules");

            migrationBuilder.DropIndex(
                name: "IX_UserStories_AgentRuleId",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "AgentRuleId",
                table: "UserStories");
        }
    }
}
