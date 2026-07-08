using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHistorianDownsamplingWindowKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "daily_window_start_utc",
                schema: "industrial_telemetry",
                table: "telemetry_rollups",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                comment: "UTC day window start used for indexed hourly-to-daily downsampling anti-joins.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "hourly_window_start_utc",
                schema: "industrial_telemetry",
                table: "telemetry_raw_samples",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                comment: "UTC hour window start used for indexed raw-to-hourly downsampling anti-joins.");

            migrationBuilder.Sql("""
                UPDATE industrial_telemetry.telemetry_raw_samples
                SET hourly_window_start_utc = date_trunc('hour', bucket_start_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC';

                UPDATE industrial_telemetry.telemetry_rollups
                SET daily_window_start_utc = date_trunc('day', window_start_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_rollups_daily_window",
                schema: "industrial_telemetry",
                table: "telemetry_rollups",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "tag_key", "grain", "daily_window_start_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_raw_samples_hourly_window",
                schema: "industrial_telemetry",
                table: "telemetry_raw_samples",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "tag_key", "hourly_window_start_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_telemetry_rollups_daily_window",
                schema: "industrial_telemetry",
                table: "telemetry_rollups");

            migrationBuilder.DropIndex(
                name: "IX_telemetry_raw_samples_hourly_window",
                schema: "industrial_telemetry",
                table: "telemetry_raw_samples");

            migrationBuilder.DropColumn(
                name: "daily_window_start_utc",
                schema: "industrial_telemetry",
                table: "telemetry_rollups");

            migrationBuilder.DropColumn(
                name: "hourly_window_start_utc",
                schema: "industrial_telemetry",
                table: "telemetry_raw_samples");
        }
    }
}
