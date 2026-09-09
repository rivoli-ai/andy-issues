using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.Issues.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTriageEstimatorModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TriageEstimatorModels",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SampleCount = table.Column<int>(type: "integer", nullable: false),
                    SampleFingerprint = table.Column<string>(type: "text", nullable: false),
                    ModelJson = table.Column<string>(type: "text", nullable: false),
                    TrainedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TriageEstimatorModels", x => new { x.TenantId, x.TemplateKey });
                });

            migrationBuilder.CreateTable(
                name: "TriageEstimatorSamples",
                columns: table => new
                {
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FeaturesJson = table.Column<string>(type: "text", nullable: false),
                    ActualCostUsd = table.Column<double>(type: "double precision", nullable: false),
                    ActualDurationHours = table.Column<double>(type: "double precision", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TriageEstimatorSamples", x => x.GoalId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TriageEstimatorSamples_TenantId_TemplateKey_RecordedAt",
                table: "TriageEstimatorSamples",
                columns: new[] { "TenantId", "TemplateKey", "RecordedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TriageEstimatorModels");

            migrationBuilder.DropTable(
                name: "TriageEstimatorSamples");
        }
    }
}
