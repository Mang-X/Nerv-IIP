using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Ops.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SchemaGovernanceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "operation_tasks",
                schema: "ops",
                comment: "Ops operation task aggregate roots requested through Gateway and executed by connector hosts.");

            migrationBuilder.AlterTable(
                name: "operation_attempts",
                schema: "ops",
                comment: "Ops operation execution attempts created when connector hosts claim operation tasks.");

            migrationBuilder.AlterTable(
                name: "audit_records",
                schema: "ops",
                comment: "Ops audit records for operation task lifecycle events and user-visible traceability.");

            migrationBuilder.AlterColumn<string>(
                name: "ParametersJson",
                schema: "ops",
                table: "operation_tasks",
                type: "text",
                nullable: false,
                comment: "JSON operation parameter dictionary produced by Gateway and Ops task creation, consumed by Connector Host execution; additive optional keys are compatible, required key or semantic changes require Ops contract versioning.",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Serialized operation parameters.");

            migrationBuilder.AlterColumn<string>(
                name: "FailureJson",
                schema: "ops",
                table: "operation_attempts",
                type: "text",
                nullable: true,
                comment: "JSON failure details produced by Connector Host execution, consumed by Ops and Gateway diagnostics; additive optional keys are compatible, removing or changing key semantics requires Ops contract versioning.",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "Serialized failure reason.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "operation_tasks",
                schema: "ops",
                oldComment: "Ops operation task aggregate roots requested through Gateway and executed by connector hosts.");

            migrationBuilder.AlterTable(
                name: "operation_attempts",
                schema: "ops",
                oldComment: "Ops operation execution attempts created when connector hosts claim operation tasks.");

            migrationBuilder.AlterTable(
                name: "audit_records",
                schema: "ops",
                oldComment: "Ops audit records for operation task lifecycle events and user-visible traceability.");

            migrationBuilder.AlterColumn<string>(
                name: "ParametersJson",
                schema: "ops",
                table: "operation_tasks",
                type: "text",
                nullable: false,
                comment: "Serialized operation parameters.",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "JSON operation parameter dictionary produced by Gateway and Ops task creation, consumed by Connector Host execution; additive optional keys are compatible, required key or semantic changes require Ops contract versioning.");

            migrationBuilder.AlterColumn<string>(
                name: "FailureJson",
                schema: "ops",
                table: "operation_attempts",
                type: "text",
                nullable: true,
                comment: "Serialized failure reason.",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "JSON failure details produced by Connector Host execution, consumed by Ops and Gateway diagnostics; additive optional keys are compatible, removing or changing key semantics requires Ops contract versioning.");
        }
    }
}
