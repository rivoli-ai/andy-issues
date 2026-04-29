using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.Issues.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTriageOutputRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TriageOutputRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    Author = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AuthorKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TriageOutputJson = table.Column<string>(type: "text", nullable: false),
                    DiffSummary = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TriageOutputRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TriageOutputRevisions_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TriageOutputRevisions_IssueId_CreatedAt",
                table: "TriageOutputRevisions",
                columns: new[] { "IssueId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TriageOutputRevisions");
        }
    }
}
