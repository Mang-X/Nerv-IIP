using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOeeHistoricalProductionFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_oee_production_facts_organization_id_environment_id_device_~",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "aggregation_occurred_at_utc",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC production instant used for historical aggregation; reversals retain the original fact instant.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "business_date",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "date",
                nullable: true,
                comment: "Site-local business date resolved from the event-time timezone and shift definition.");

            migrationBuilder.AddColumn<string>(
                name: "historical_dimension_status",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                comment: "Historical dimension resolution status; LegacyUnresolved identifies facts migrated from the prior schema.");

            migrationBuilder.AddColumn<string>(
                name: "line_code",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Event-time MES production-line snapshot; null when the historical hierarchy was unavailable.");

            migrationBuilder.AddColumn<string>(
                name: "reversed_report_no",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Original MES report number reversed by this fact; null for ordinary reports.");

            migrationBuilder.AddColumn<int>(
                name: "shift_break_minutes",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "integer",
                nullable: true,
                comment: "Planned break minutes from the event-time shift definition.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "shift_bucket_end_utc",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Resolved UTC end of the event-time shift bucket; null when resolution degraded.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "shift_bucket_start_utc",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Resolved UTC start of the event-time shift bucket; null when resolution degraded.");

            migrationBuilder.AddColumn<string>(
                name: "shift_code",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Event-time MES shift code; null when the historical shift was unavailable.");

            migrationBuilder.AddColumn<bool>(
                name: "shift_crosses_midnight",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "boolean",
                nullable: true,
                comment: "Whether the event-time shift definition ends on the next local business day.");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "shift_ends_at",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "time without time zone",
                nullable: true,
                comment: "Local wall-clock end from the MES event-time shift definition.");

            migrationBuilder.AddColumn<int>(
                name: "shift_paid_minutes",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "integer",
                nullable: true,
                comment: "Paid or planned working minutes from the event-time shift definition.");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "shift_starts_at",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "time without time zone",
                nullable: true,
                comment: "Local wall-clock start from the MES event-time shift definition.");

            migrationBuilder.AddColumn<string>(
                name: "site_code",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Event-time MES site snapshot; null when the historical hierarchy was unavailable.");

            migrationBuilder.AddColumn<string>(
                name: "site_timezone",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "IANA site timezone copied from the MES event-time snapshot.");

            migrationBuilder.AddColumn<string>(
                name: "workshop_code",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Event-time MES workshop snapshot; null when the historical hierarchy was unavailable.");

            migrationBuilder.Sql(
                """
                UPDATE industrial_telemetry.oee_production_facts
                SET aggregation_occurred_at_utc = reported_at_utc,
                    historical_dimension_status = 'LegacyUnresolved';
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "aggregation_occurred_at_utc",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "timestamp with time zone",
                nullable: false,
                comment: "UTC production instant used for historical aggregation; reversals retain the original fact instant.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "UTC production instant used for historical aggregation; reversals retain the original fact instant.");

            migrationBuilder.AlterColumn<string>(
                name: "historical_dimension_status",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                comment: "Historical dimension resolution status; LegacyUnresolved identifies facts migrated from the prior schema.",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true,
                oldComment: "Historical dimension resolution status; LegacyUnresolved identifies facts migrated from the prior schema.");

            migrationBuilder.CreateIndex(
                name: "ix_oee_production_facts_scope_device_aggregation",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "aggregation_occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_oee_production_facts_scope_hierarchy_business_shift",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                columns: new[] { "organization_id", "environment_id", "site_code", "workshop_code", "line_code", "business_date", "shift_code" });

            migrationBuilder.CreateIndex(
                name: "ix_oee_production_facts_scope_work_center_aggregation",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                columns: new[] { "organization_id", "environment_id", "work_center_id", "aggregation_occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM industrial_telemetry.oee_production_facts
                WHERE historical_dimension_status NOT IN ('Resolved', 'LegacyUnresolved')
                   OR reversed_report_no IS NOT NULL
                   OR aggregation_occurred_at_utc <> reported_at_utc;
                """);

            migrationBuilder.DropIndex(
                name: "ix_oee_production_facts_scope_device_aggregation",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropIndex(
                name: "ix_oee_production_facts_scope_hierarchy_business_shift",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropIndex(
                name: "ix_oee_production_facts_scope_work_center_aggregation",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "aggregation_occurred_at_utc",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "business_date",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "historical_dimension_status",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "line_code",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "reversed_report_no",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "shift_break_minutes",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "shift_bucket_end_utc",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "shift_bucket_start_utc",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "shift_code",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "shift_crosses_midnight",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "shift_ends_at",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "shift_paid_minutes",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "shift_starts_at",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "site_code",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "site_timezone",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "workshop_code",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.CreateIndex(
                name: "IX_oee_production_facts_organization_id_environment_id_device_~",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "reported_at_utc" });
        }
    }
}
