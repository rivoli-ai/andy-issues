using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.Issues.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAzureDevOpsWorkItemIdToUserStory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AzureDevOpsWorkItemId",
                table: "UserStories",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserStories_AzureDevOpsWorkItemId",
                table: "UserStories",
                column: "AzureDevOpsWorkItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserStories_AzureDevOpsWorkItemId",
                table: "UserStories");

            migrationBuilder.DropColumn(
                name: "AzureDevOpsWorkItemId",
                table: "UserStories");
        }
    }
}
