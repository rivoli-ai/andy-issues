using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.Issues.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueTriageRunId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TriageRunId",
                table: "Issues",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Issues_TriageRunId",
                table: "Issues",
                column: "TriageRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Issues_TriageRunId",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "TriageRunId",
                table: "Issues");
        }
    }
}
