using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeSourceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_telemetry_summaries_organization_id_environment_id_device_~1",
                schema: "industrial_telemetry",
                table: "telemetry_summaries");

            migrationBuilder.DropIndex(
                name: "IX_device_state_snapshots_organization_id_environment_id_devi~1",
                schema: "industrial_telemetry",
                table: "device_state_snapshots");

            migrationBuilder.AddColumn<string>(
                name: "source_connector",
                schema: "industrial_telemetry",
                table: "telemetry_summaries",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Connector instance or adapter that delivered the telemetry summary.");

            migrationBuilder.AddColumn<string>(
                name: "source_system",
                schema: "industrial_telemetry",
                table: "telemetry_summaries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "External source system that produced the telemetry summary.");

            migrationBuilder.AddColumn<string>(
                name: "source_connector",
                schema: "industrial_telemetry",
                table: "device_state_snapshots",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Connector instance or adapter that delivered the device state.");

            migrationBuilder.AddColumn<string>(
                name: "source_system",
                schema: "industrial_telemetry",
                table: "device_state_snapshots",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "External source system that observed the device state.");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_summaries_organization_id_environment_id_source_s~",
                schema: "industrial_telemetry",
                table: "telemetry_summaries",
                columns: new[] { "organization_id", "environment_id", "source_system", "source_connector", "device_asset_id", "tag_key", "source_sequence" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_device_state_snapshots_organization_id_environment_id_sourc~",
                schema: "industrial_telemetry",
                table: "device_state_snapshots",
                columns: new[] { "organization_id", "environment_id", "source_system", "source_connector", "device_asset_id", "source_sequence" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_telemetry_summaries_organization_id_environment_id_source_s~",
                schema: "industrial_telemetry",
                table: "telemetry_summaries");

            migrationBuilder.DropIndex(
                name: "IX_device_state_snapshots_organization_id_environment_id_sourc~",
                schema: "industrial_telemetry",
                table: "device_state_snapshots");

            migrationBuilder.DropColumn(
                name: "source_connector",
                schema: "industrial_telemetry",
                table: "telemetry_summaries");

            migrationBuilder.DropColumn(
                name: "source_system",
                schema: "industrial_telemetry",
                table: "telemetry_summaries");

            migrationBuilder.DropColumn(
                name: "source_connector",
                schema: "industrial_telemetry",
                table: "device_state_snapshots");

            migrationBuilder.DropColumn(
                name: "source_system",
                schema: "industrial_telemetry",
                table: "device_state_snapshots");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_summaries_organization_id_environment_id_device_~1",
                schema: "industrial_telemetry",
                table: "telemetry_summaries",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "tag_key", "source_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_state_snapshots_organization_id_environment_id_devi~1",
                schema: "industrial_telemetry",
                table: "device_state_snapshots",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "source_sequence" },
                unique: true);
        }
    }
}
