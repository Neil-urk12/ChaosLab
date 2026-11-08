using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScenariosAndTargetSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeveloperInvariant",
                table: "ScenarioExecutions");

            migrationBuilder.DropColumn(
                name: "DeveloperInvariant",
                table: "ChaosScenarios");

            migrationBuilder.AddColumn<JsonDocument>(
                name: "Invariants",
                table: "ScenarioExecutions",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<JsonDocument>(
                name: "Invariants",
                table: "ChaosScenarios",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "ChaosScenarios",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AuthorizationConfirmedAt",
                table: "ApiEnvironments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AuthorizationConfirmedById",
                table: "ApiEnvironments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "ApiEnvironments",
                type: "integer",
                nullable: false,
                defaultValue: 1); // EnvironmentKind.Test

            migrationBuilder.AddColumn<int>(
                name: "RequestTimeoutSeconds",
                table: "ApiEnvironments",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.CreateTable(
                name: "EnvironmentHeaders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    SecretCipherText = table.Column<byte[]>(type: "bytea", nullable: true),
                    Hint = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    AppliesTo = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnvironmentHeaders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnvironmentHeaders_ApiEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "ApiEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChaosScenarios_EndpointId_ScenarioType",
                table: "ChaosScenarios",
                columns: new[] { "EndpointId", "ScenarioType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiEnvironments_AuthorizationConfirmedById",
                table: "ApiEnvironments",
                column: "AuthorizationConfirmedById");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApiEnvironments_RequestTimeoutSeconds",
                table: "ApiEnvironments",
                sql: "\"RequestTimeoutSeconds\" BETWEEN 1 AND 300");

            migrationBuilder.CreateIndex(
                name: "IX_EnvironmentHeaders_EnvironmentId_Name",
                table: "EnvironmentHeaders",
                columns: new[] { "EnvironmentId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ApiEnvironments_AspNetUsers_AuthorizationConfirmedById",
                table: "ApiEnvironments",
                column: "AuthorizationConfirmedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApiEnvironments_AspNetUsers_AuthorizationConfirmedById",
                table: "ApiEnvironments");

            migrationBuilder.DropTable(
                name: "EnvironmentHeaders");

            migrationBuilder.DropIndex(
                name: "IX_ChaosScenarios_EndpointId_ScenarioType",
                table: "ChaosScenarios");

            migrationBuilder.DropIndex(
                name: "IX_ApiEnvironments_AuthorizationConfirmedById",
                table: "ApiEnvironments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ApiEnvironments_RequestTimeoutSeconds",
                table: "ApiEnvironments");

            migrationBuilder.DropColumn(
                name: "Invariants",
                table: "ScenarioExecutions");

            migrationBuilder.DropColumn(
                name: "Invariants",
                table: "ChaosScenarios");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "ChaosScenarios");

            migrationBuilder.DropColumn(
                name: "AuthorizationConfirmedAt",
                table: "ApiEnvironments");

            migrationBuilder.DropColumn(
                name: "AuthorizationConfirmedById",
                table: "ApiEnvironments");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ApiEnvironments");

            migrationBuilder.DropColumn(
                name: "RequestTimeoutSeconds",
                table: "ApiEnvironments");

            migrationBuilder.AddColumn<string>(
                name: "DeveloperInvariant",
                table: "ScenarioExecutions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeveloperInvariant",
                table: "ChaosScenarios",
                type: "text",
                nullable: true);
        }
    }
}
