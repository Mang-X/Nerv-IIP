using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionReportOeeHistoricalDimensionSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "oee_line_code",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "MasterData production line code snapshot captured when the production report was recorded.");

            migrationBuilder.AddColumn<int>(
                name: "oee_shift_break_minutes",
                schema: "mes",
                table: "production_reports",
                type: "integer",
                nullable: true,
                comment: "Break minutes from the captured shift definition.");

            migrationBuilder.AddColumn<string>(
                name: "oee_shift_code",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Assigned MasterData shift code snapshot captured when the production report was recorded.");

            migrationBuilder.AddColumn<bool>(
                name: "oee_shift_crosses_midnight",
                schema: "mes",
                table: "production_reports",
                type: "boolean",
                nullable: true,
                comment: "Whether the captured shift definition crosses local midnight.");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "oee_shift_ends_at",
                schema: "mes",
                table: "production_reports",
                type: "time without time zone",
                nullable: true,
                comment: "Local shift end time snapshot used to derive the historical shift window.");

            migrationBuilder.AddColumn<int>(
                name: "oee_shift_paid_minutes",
                schema: "mes",
                table: "production_reports",
                type: "integer",
                nullable: true,
                comment: "Paid minutes from the captured shift definition.");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "oee_shift_starts_at",
                schema: "mes",
                table: "production_reports",
                type: "time without time zone",
                nullable: true,
                comment: "Local shift start time snapshot used to derive the historical shift window.");

            migrationBuilder.AddColumn<string>(
                name: "oee_site_code",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "MasterData site code snapshot captured when the production report was recorded.");

            migrationBuilder.AddColumn<string>(
                name: "oee_site_timezone",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "IANA site timezone snapshot used to derive historical OEE business-day boundaries.");

            migrationBuilder.AddColumn<string>(
                name: "oee_workshop_code",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "MasterData workshop code snapshot captured when the production report was recorded.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "oee_line_code",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "oee_shift_break_minutes",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "oee_shift_code",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "oee_shift_crosses_midnight",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "oee_shift_ends_at",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "oee_shift_paid_minutes",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "oee_shift_starts_at",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "oee_site_code",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "oee_site_timezone",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "oee_workshop_code",
                schema: "mes",
                table: "production_reports");
        }
    }
}
