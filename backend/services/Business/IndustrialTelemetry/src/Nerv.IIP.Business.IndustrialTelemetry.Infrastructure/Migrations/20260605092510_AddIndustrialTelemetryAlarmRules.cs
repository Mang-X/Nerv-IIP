using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIndustrialTelemetryAlarmRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alarm_rules",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Alarm rule identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning organization identifier."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning environment identifier."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Referenced MasterData device asset identifier."),
                    rule_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Stable alarm rule code unique within a device."),
                    alarm_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Alarm code raised when the rule condition is met."),
                    severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Alarm severity emitted by this rule."),
                    tag_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Telemetry tag key evaluated by this alarm rule."),
                    comparison_operator = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, comment: "Threshold comparison operator such as >= or <."),
                    threshold_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Numeric threshold value used by the rule condition."),
                    unit_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Unit of measure code for the threshold."),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the alarm rule is enabled for evaluation."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the alarm rule was created."),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the alarm rule was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alarm_rules", x => x.Id);
                },
                comment: "BusinessIndustrialTelemetry alarm rule threshold configuration.");

            migrationBuilder.CreateIndex(
                name: "IX_alarm_rules_organization_id_environment_id_device_asset_id_~",
                schema: "industrial_telemetry",
                table: "alarm_rules",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "rule_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alarm_rules_organization_id_environment_id_device_asset_id~1",
                schema: "industrial_telemetry",
                table: "alarm_rules",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "tag_key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alarm_rules",
                schema: "industrial_telemetry");
        }
    }
}
