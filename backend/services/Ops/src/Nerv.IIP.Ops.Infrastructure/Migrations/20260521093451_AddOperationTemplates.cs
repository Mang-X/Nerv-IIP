using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Ops.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultLeaseDurationSeconds",
                schema: "ops",
                table: "operation_tasks",
                type: "integer",
                nullable: false,
                defaultValue: 300,
                comment: "Template-provided default connector lease duration captured at task creation.");

            migrationBuilder.AddColumn<int>(
                name: "DefaultMaxAttempts",
                schema: "ops",
                table: "operation_tasks",
                type: "integer",
                nullable: false,
                defaultValue: 3,
                comment: "Template-provided default maximum execution attempts captured at task creation.");

            migrationBuilder.AddColumn<bool>(
                name: "RequiresApproval",
                schema: "ops",
                table: "operation_tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether the selected template requires approval before task execution.");

            migrationBuilder.CreateTable(
                name: "operation_templates",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Operation template identifier."),
                    OperationCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Registered operation code."),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Operation display name."),
                    ParameterSchemaJson = table.Column<string>(type: "text", nullable: false, comment: "JSON schema describing accepted operation parameters."),
                    RiskLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Operation risk level."),
                    DefaultMaxAttempts = table.Column<int>(type: "integer", nullable: false, comment: "Default maximum execution attempts."),
                    DefaultLeaseDurationSeconds = table.Column<int>(type: "integer", nullable: false, comment: "Default connector lease duration in seconds."),
                    RequiresApproval = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this template requires manual approval before execution."),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this operation template can create new tasks."),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Template creation time in UTC."),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Template last update time in UTC."),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag."),
                    RowVersion = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_templates", x => x.Id);
                },
                comment: "Ops operation templates registering supported operation codes and execution defaults.");

            migrationBuilder.CreateIndex(
                name: "IX_operation_templates_Enabled_RiskLevel",
                schema: "ops",
                table: "operation_templates",
                columns: new[] { "Enabled", "RiskLevel" });

            migrationBuilder.CreateIndex(
                name: "IX_operation_templates_OperationCode",
                schema: "ops",
                table: "operation_templates",
                column: "OperationCode",
                unique: true);

            migrationBuilder.InsertData(
                schema: "ops",
                table: "operation_templates",
                columns: new[]
                {
                    "Id",
                    "OperationCode",
                    "DisplayName",
                    "ParameterSchemaJson",
                    "RiskLevel",
                    "DefaultMaxAttempts",
                    "DefaultLeaseDurationSeconds",
                    "RequiresApproval",
                    "Enabled",
                    "CreatedAtUtc",
                    "UpdatedAtUtc",
                    "Deleted",
                    "RowVersion"
                },
                values: new object[]
                {
                    "opt-lifecycle-restart",
                    "lifecycle.restart",
                    "Lifecycle restart",
                    "{}",
                    "low",
                    3,
                    300,
                    false,
                    true,
                    new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, DateTimeKind.Utc)),
                    new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, DateTimeKind.Utc)),
                    false,
                    0
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operation_templates",
                schema: "ops");

            migrationBuilder.DropColumn(
                name: "DefaultLeaseDurationSeconds",
                schema: "ops",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "DefaultMaxAttempts",
                schema: "ops",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "RequiresApproval",
                schema: "ops",
                table: "operation_tasks");
        }
    }
}
