using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRunScoreBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<JsonDocument>(
                name: "CategoryScores",
                table: "TestRuns",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EvidenceCoverage",
                table: "TestRuns",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TestRuns_EvidenceCoverage",
                table: "TestRuns",
                sql: "\"EvidenceCoverage\" BETWEEN 0 AND 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TestRuns_EvidenceCoverage",
                table: "TestRuns");

            migrationBuilder.DropColumn(
                name: "CategoryScores",
                table: "TestRuns");

            migrationBuilder.DropColumn(
                name: "EvidenceCoverage",
                table: "TestRuns");
        }
    }
}
