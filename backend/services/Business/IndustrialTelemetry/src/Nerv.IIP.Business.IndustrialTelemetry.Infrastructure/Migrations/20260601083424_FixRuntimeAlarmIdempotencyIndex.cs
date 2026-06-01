using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixRuntimeAlarmIdempotencyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_alarm_events_organization_id_environment_id_external_alarm_~",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.CreateIndex(
                name: "IX_alarm_events_organization_id_environment_id_device_asset_i~1",
                schema: "industrial_telemetry",
                table: "alarm_events",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "alarm_code", "external_alarm_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_alarm_events_organization_id_environment_id_device_asset_i~1",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.CreateIndex(
                name: "IX_alarm_events_organization_id_environment_id_external_alarm_~",
                schema: "industrial_telemetry",
                table: "alarm_events",
                columns: new[] { "organization_id", "environment_id", "external_alarm_id" },
                unique: true);
        }
    }
}
