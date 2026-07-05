using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceStateSnapshotSortKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "occurred_at_unix_time_milliseconds",
                schema: "industrial_telemetry",
                table: "device_state_snapshots",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Observed UTC time represented as Unix time milliseconds for provider-neutral current-state ordering.");

            migrationBuilder.AddColumn<long>(
                name: "recorded_at_unix_time_milliseconds",
                schema: "industrial_telemetry",
                table: "device_state_snapshots",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Recorded UTC time represented as Unix time milliseconds for provider-neutral current-state ordering.");

            migrationBuilder.Sql(
                """
                UPDATE industrial_telemetry.device_state_snapshots
                SET occurred_at_unix_time_milliseconds = (EXTRACT(EPOCH FROM occurred_at_utc) * 1000)::bigint,
                    recorded_at_unix_time_milliseconds = (EXTRACT(EPOCH FROM recorded_at_utc) * 1000)::bigint;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_device_state_snapshots_current_state_sort",
                schema: "industrial_telemetry",
                table: "device_state_snapshots",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "occurred_at_unix_time_milliseconds", "recorded_at_unix_time_milliseconds", "source_sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_device_state_snapshots_current_state_sort",
                schema: "industrial_telemetry",
                table: "device_state_snapshots");

            migrationBuilder.DropColumn(
                name: "occurred_at_unix_time_milliseconds",
                schema: "industrial_telemetry",
                table: "device_state_snapshots");

            migrationBuilder.DropColumn(
                name: "recorded_at_unix_time_milliseconds",
                schema: "industrial_telemetry",
                table: "device_state_snapshots");
        }
    }
}
