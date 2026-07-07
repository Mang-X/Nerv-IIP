using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Ops.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PersistOperationAttemptOutput : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ParameterSchemaJson",
                schema: "ops",
                table: "operation_templates",
                type: "text",
                nullable: false,
                comment: "JSON schema produced by Ops operation type registration and consumed by Gateway or Connector Host clients to validate operation parameters; additive optional schema fields are compatible, required field or semantic changes require Ops contract versioning.",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "JSON schema describing accepted operation parameters.");

            migrationBuilder.AddColumn<string>(
                name: "OutputJson",
                schema: "ops",
                table: "operation_attempts",
                type: "text",
                nullable: true,
                comment: "JSON connector execution output and device receipt metadata produced by Connector Host; additive optional keys are compatible, removing or changing key semantics requires Ops contract versioning.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutputJson",
                schema: "ops",
                table: "operation_attempts");

            migrationBuilder.AlterColumn<string>(
                name: "ParameterSchemaJson",
                schema: "ops",
                table: "operation_templates",
                type: "text",
                nullable: false,
                comment: "JSON schema describing accepted operation parameters.",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "JSON schema produced by Ops operation type registration and consumed by Gateway or Connector Host clients to validate operation parameters; additive optional schema fields are compatible, required field or semantic changes require Ops contract versioning.");
        }
    }
}
