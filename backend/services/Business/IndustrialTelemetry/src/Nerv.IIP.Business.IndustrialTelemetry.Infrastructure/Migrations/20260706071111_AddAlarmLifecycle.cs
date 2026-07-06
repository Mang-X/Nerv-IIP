using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlarmLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_alarm_events_organization_id_environment_id_device_asset_i~1",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.DropIndex(
                name: "IX_alarm_events_organization_id_environment_id_device_asset_i~2",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "acknowledged_at_utc",
                schema: "industrial_telemetry",
                table: "alarm_events",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when an operator acknowledged the active alarm.");

            migrationBuilder.AddColumn<string>(
                name: "acknowledged_by",
                schema: "industrial_telemetry",
                table: "alarm_events",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Actor that acknowledged the active alarm.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "escalated_at_utc",
                schema: "industrial_telemetry",
                table: "alarm_events",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the alarm was escalated.");

            migrationBuilder.AddColumn<string>(
                name: "escalation_reason",
                schema: "industrial_telemetry",
                table: "alarm_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Reason code that triggered alarm escalation.");

            migrationBuilder.AddColumn<string>(
                name: "escalation_recipient_refs",
                schema: "industrial_telemetry",
                table: "alarm_events",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "Semicolon-separated Notification recipient refs used for alarm escalation.");

            migrationBuilder.AddColumn<string>(
                name: "shelve_reason",
                schema: "industrial_telemetry",
                table: "alarm_events",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                comment: "Reason recorded when the alarm was shelved.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "shelved_at_utc",
                schema: "industrial_telemetry",
                table: "alarm_events",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the alarm was temporarily shelved.");

            migrationBuilder.AddColumn<string>(
                name: "shelved_by",
                schema: "industrial_telemetry",
                table: "alarm_events",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Actor that shelved the alarm.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "shelved_until_utc",
                schema: "industrial_telemetry",
                table: "alarm_events",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC expiry time for temporary alarm shelving.");

            migrationBuilder.CreateIndex(
                name: "IX_alarm_events_organization_id_environment_id_device_asset_i~1",
                schema: "industrial_telemetry",
                table: "alarm_events",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "alarm_code", "external_alarm_id" },
                unique: true,
                filter: "status <> 'cleared'");

            migrationBuilder.CreateIndex(
                name: "IX_alarm_events_organization_id_environment_id_device_asset_i~2",
                schema: "industrial_telemetry",
                table: "alarm_events",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "tag_key", "external_alarm_id" },
                unique: true,
                filter: "status <> 'cleared' AND tag_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_alarm_events_organization_id_environment_id_device_asset_i~1",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.DropIndex(
                name: "IX_alarm_events_organization_id_environment_id_device_asset_i~2",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.DropColumn(
                name: "acknowledged_at_utc",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.DropColumn(
                name: "acknowledged_by",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.DropColumn(
                name: "escalated_at_utc",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.DropColumn(
                name: "escalation_reason",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.DropColumn(
                name: "escalation_recipient_refs",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.DropColumn(
                name: "shelve_reason",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.DropColumn(
                name: "shelved_at_utc",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.DropColumn(
                name: "shelved_by",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.DropColumn(
                name: "shelved_until_utc",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.CreateIndex(
                name: "IX_alarm_events_organization_id_environment_id_device_asset_i~1",
                schema: "industrial_telemetry",
                table: "alarm_events",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "alarm_code", "external_alarm_id" },
                unique: true,
                filter: "status = 'raised'");

            migrationBuilder.CreateIndex(
                name: "IX_alarm_events_organization_id_environment_id_device_asset_i~2",
                schema: "industrial_telemetry",
                table: "alarm_events",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "tag_key", "external_alarm_id" },
                unique: true,
                filter: "status = 'raised' AND tag_key IS NOT NULL");
        }
    }
}
