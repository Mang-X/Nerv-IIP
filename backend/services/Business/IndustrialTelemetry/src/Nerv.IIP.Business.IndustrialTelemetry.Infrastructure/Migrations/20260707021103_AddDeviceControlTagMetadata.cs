using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceControlTagMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "control_allowed_values_json",
                schema: "industrial_telemetry",
                table: "telemetry_tags",
                type: "text",
                nullable: false,
                defaultValue: "[]",
                comment: "JSON array of optional allowed literal values for device control writes; produced by IndustrialTelemetry tag metadata and consumed by device control validation, additive values are compatible.");

            migrationBuilder.AddColumn<decimal>(
                name: "control_max_value",
                schema: "industrial_telemetry",
                table: "telemetry_tags",
                type: "numeric",
                nullable: true,
                comment: "Optional maximum allowed control value for numeric device control writes.");

            migrationBuilder.AddColumn<decimal>(
                name: "control_min_value",
                schema: "industrial_telemetry",
                table: "telemetry_tags",
                type: "numeric",
                nullable: true,
                comment: "Optional minimum allowed control value for numeric device control writes.");

            migrationBuilder.AddColumn<bool>(
                name: "is_writable",
                schema: "industrial_telemetry",
                table: "telemetry_tags",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether this telemetry tag may be used as a validated device control write target.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "control_allowed_values_json",
                schema: "industrial_telemetry",
                table: "telemetry_tags");

            migrationBuilder.DropColumn(
                name: "control_max_value",
                schema: "industrial_telemetry",
                table: "telemetry_tags");

            migrationBuilder.DropColumn(
                name: "control_min_value",
                schema: "industrial_telemetry",
                table: "telemetry_tags");

            migrationBuilder.DropColumn(
                name: "is_writable",
                schema: "industrial_telemetry",
                table: "telemetry_tags");
        }
    }
}
