using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveAlarmRuleUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH duplicate_external_alarms AS (
                    SELECT "Id" AS alarm_id,
                        ROW_NUMBER() OVER (
                            PARTITION BY organization_id, environment_id, device_asset_id, alarm_code, external_alarm_id
                            ORDER BY raised_at_utc, recorded_at_utc, "Id") AS duplicate_rank
                    FROM industrial_telemetry.alarm_events
                    WHERE status = 'raised'
                )
                UPDATE industrial_telemetry.alarm_events alarm
                SET status = 'cleared',
                    cleared_at_utc = GREATEST(alarm.raised_at_utc, CURRENT_TIMESTAMP),
                    cleared_by = 'system:industrial-telemetry',
                    clear_reason = 'duplicate-active-alarm-suppressed-by-migration'
                FROM duplicate_external_alarms duplicate
                WHERE alarm."Id" = duplicate.alarm_id
                    AND duplicate.duplicate_rank > 1;
                """);

            migrationBuilder.Sql(
                """
                WITH duplicate_rule_alarms AS (
                    SELECT "Id" AS alarm_id,
                        ROW_NUMBER() OVER (
                            PARTITION BY organization_id, environment_id, device_asset_id, tag_key, external_alarm_id
                            ORDER BY raised_at_utc, recorded_at_utc, "Id") AS duplicate_rank
                    FROM industrial_telemetry.alarm_events
                    WHERE status = 'raised'
                        AND tag_key IS NOT NULL
                )
                UPDATE industrial_telemetry.alarm_events alarm
                SET status = 'cleared',
                    cleared_at_utc = GREATEST(alarm.raised_at_utc, CURRENT_TIMESTAMP),
                    cleared_by = 'system:industrial-telemetry',
                    clear_reason = 'duplicate-rule-alarm-suppressed-by-migration'
                FROM duplicate_rule_alarms duplicate
                WHERE alarm."Id" = duplicate.alarm_id
                    AND duplicate.duplicate_rank > 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_alarm_events_organization_id_environment_id_device_asset_i~1",
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("AddActiveAlarmRuleUniqueness cannot be safely rolled back because raised-only uniqueness permits historical cleared duplicates.");
        }
    }
}
