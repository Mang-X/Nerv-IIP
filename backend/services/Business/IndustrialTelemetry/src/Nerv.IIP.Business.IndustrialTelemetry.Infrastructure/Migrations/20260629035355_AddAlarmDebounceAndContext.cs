using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlarmDebounceAndContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "deadband_value",
                schema: "industrial_telemetry",
                table: "alarm_rules",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Deadband value applied before clearing a threshold alarm.");

            migrationBuilder.AddColumn<int>(
                name: "min_duration_seconds",
                schema: "industrial_telemetry",
                table: "alarm_rules",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Minimum breach duration seconds required before raising the alarm.");

            migrationBuilder.AddColumn<int>(
                name: "off_delay_seconds",
                schema: "industrial_telemetry",
                table: "alarm_rules",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Continuous return-to-normal seconds required before clearing the alarm.");

            migrationBuilder.AddColumn<int>(
                name: "on_delay_seconds",
                schema: "industrial_telemetry",
                table: "alarm_rules",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Continuous breach seconds required before raising the alarm.");

            migrationBuilder.AddColumn<string>(
                name: "priority",
                schema: "industrial_telemetry",
                table: "alarm_rules",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "Independent alarm priority, separate from severity.");

            migrationBuilder.AddColumn<decimal>(
                name: "observed_value",
                schema: "industrial_telemetry",
                table: "alarm_events",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Observed process value that raised this alarm.");

            migrationBuilder.AddColumn<string>(
                name: "priority",
                schema: "industrial_telemetry",
                table: "alarm_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "Independent alarm priority.");

            migrationBuilder.AddColumn<string>(
                name: "tag_key",
                schema: "industrial_telemetry",
                table: "alarm_events",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Telemetry tag key whose observed value raised this alarm.");

            migrationBuilder.AddColumn<decimal>(
                name: "threshold_value",
                schema: "industrial_telemetry",
                table: "alarm_events",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Rule threshold value when this alarm was raised.");

            migrationBuilder.AddColumn<string>(
                name: "unit_code",
                schema: "industrial_telemetry",
                table: "alarm_events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Observed and threshold unit code.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deadband_value",
                schema: "industrial_telemetry",
                table: "alarm_rules");

            migrationBuilder.DropColumn(
                name: "min_duration_seconds",
                schema: "industrial_telemetry",
                table: "alarm_rules");

            migrationBuilder.DropColumn(
                name: "off_delay_seconds",
                schema: "industrial_telemetry",
                table: "alarm_rules");

            migrationBuilder.DropColumn(
                name: "on_delay_seconds",
                schema: "industrial_telemetry",
                table: "alarm_rules");

            migrationBuilder.DropColumn(
                name: "priority",
                schema: "industrial_telemetry",
                table: "alarm_rules");

            migrationBuilder.DropColumn(
                name: "observed_value",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.DropColumn(
                name: "priority",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.DropColumn(
                name: "tag_key",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.DropColumn(
                name: "threshold_value",
                schema: "industrial_telemetry",
                table: "alarm_events");

            migrationBuilder.DropColumn(
                name: "unit_code",
                schema: "industrial_telemetry",
                table: "alarm_events");
        }
    }
}
