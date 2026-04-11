using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.Issues.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDirectoryEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserDirectory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDirectory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserDirectory_Email",
                table: "UserDirectory",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDirectory_UserId",
                table: "UserDirectory",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserDirectory");
        }
    }
}
