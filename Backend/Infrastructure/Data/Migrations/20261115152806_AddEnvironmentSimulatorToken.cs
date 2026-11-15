using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEnvironmentSimulatorToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SimulatorToken",
                table: "ApiEnvironments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Environments that already name a dependency get a token, or their simulator route would stop working.
            // Two v4 UUIDs (strong random) give 64 hex characters, the format DependencySimulator.NewToken produces.
            migrationBuilder.Sql("""
                UPDATE "ApiEnvironments"
                SET "SimulatorToken" = replace(gen_random_uuid()::text || gen_random_uuid()::text, '-', '')
                WHERE "DependencyUrl" IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ApiEnvironments_SimulatorToken",
                table: "ApiEnvironments",
                column: "SimulatorToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApiEnvironments_SimulatorToken",
                table: "ApiEnvironments");

            migrationBuilder.DropColumn(
                name: "SimulatorToken",
                table: "ApiEnvironments");
        }
    }
}
