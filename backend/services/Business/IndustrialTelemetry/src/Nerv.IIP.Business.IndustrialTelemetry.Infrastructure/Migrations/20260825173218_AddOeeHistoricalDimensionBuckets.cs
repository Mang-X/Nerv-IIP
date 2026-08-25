using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOeeHistoricalDimensionBuckets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_oee_production_facts_organization_id_environment_id_device_~",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                newName: "ix_oee_production_facts_scope_device_reported_at");

            migrationBuilder.AddColumn<DateOnly>(
                name: "business_date",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "date",
                nullable: true,
                comment: "Site-local calendar date containing the report.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "day_bucket_end_utc",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC end of the captured site-local business day.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "day_bucket_start_utc",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC start of the captured site-local business day.");

            migrationBuilder.AddColumn<string>(
                name: "line_code",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "MasterData production line code snapshot captured by MES when the report was recorded.");

            migrationBuilder.AddColumn<int>(
                name: "shift_break_minutes",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "integer",
                nullable: true,
                comment: "Break minutes from the captured shift definition.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "shift_bucket_end_utc",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC end of the captured shift instance.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "shift_bucket_start_utc",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC start of the captured shift instance.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "shift_business_date",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "date",
                nullable: true,
                comment: "Local date on which the captured shift instance starts.");

            migrationBuilder.AddColumn<string>(
                name: "shift_code",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Assigned MasterData shift code snapshot captured by MES when the report was recorded.");

            migrationBuilder.AddColumn<bool>(
                name: "shift_crosses_midnight",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "boolean",
                nullable: true,
                comment: "Whether the captured shift definition crosses local midnight.");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "shift_ends_at",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "time without time zone",
                nullable: true,
                comment: "Captured local shift end time.");

            migrationBuilder.AddColumn<int>(
                name: "shift_paid_minutes",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "integer",
                nullable: true,
                comment: "Paid minutes from the captured shift definition.");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "shift_starts_at",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "time without time zone",
                nullable: true,
                comment: "Captured local shift start time.");

            migrationBuilder.AddColumn<string>(
                name: "site_code",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "MasterData site code snapshot captured by MES when the report was recorded.");

            migrationBuilder.AddColumn<string>(
                name: "site_timezone",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "IANA site timezone snapshot used for historical day and shift boundaries.");

            migrationBuilder.AddColumn<string>(
                name: "workshop_code",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "MasterData workshop code snapshot captured by MES when the report was recorded.");

            migrationBuilder.CreateIndex(
                name: "ix_oee_production_facts_scope_day_bucket",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                columns: new[] { "organization_id", "environment_id", "site_code", "day_bucket_start_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_oee_production_facts_scope_line_reported_at",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                columns: new[] { "organization_id", "environment_id", "line_code", "reported_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_oee_production_facts_scope_shift_bucket",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                columns: new[] { "organization_id", "environment_id", "shift_code", "shift_bucket_start_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_oee_production_facts_scope_work_center_reported_at",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                columns: new[] { "organization_id", "environment_id", "work_center_id", "reported_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_oee_production_facts_scope_workshop_reported_at",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                columns: new[] { "organization_id", "environment_id", "workshop_code", "reported_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_oee_production_facts_scope_line_reported_at",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropIndex(
                name: "ix_oee_production_facts_scope_day_bucket",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropIndex(
                name: "ix_oee_production_facts_scope_shift_bucket",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropIndex(
                name: "ix_oee_production_facts_scope_work_center_reported_at",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropIndex(
                name: "ix_oee_production_facts_scope_workshop_reported_at",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "business_date",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "day_bucket_end_utc",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "day_bucket_start_utc",
                schema: "industrial_telemetry",
                table: "oee_production_facts");

            migrationBuilder.DropColumn(
                name: "line_code",
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
                name: "shift_business_date",
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

            migrationBuilder.RenameIndex(
                name: "ix_oee_production_facts_scope_device_reported_at",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                newName: "IX_oee_production_facts_organization_id_environment_id_device_~");
        }
    }
}
