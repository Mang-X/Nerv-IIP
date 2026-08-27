using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesProductionReportOeeDimensionSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "oee_dimension_degraded_reason",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Explicit reason why the event-time OEE dimension snapshot is degraded; NULL for resolved and legacy rows.");

            migrationBuilder.AddColumn<string>(
                name: "oee_dimension_resolution_status",
                schema: "mes",
                table: "production_reports",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                defaultValue: "resolved",
                comment: "Event-time OEE dimension resolution outcome: resolved or degraded; NULL marks legacy rows predating the snapshot contract.");

            migrationBuilder.AddColumn<string>(
                name: "oee_line_code",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Authoritative MasterData production line code captured with the production report.");

            migrationBuilder.AddColumn<int>(
                name: "oee_shift_break_minutes",
                schema: "mes",
                table: "production_reports",
                type: "integer",
                nullable: true,
                comment: "Break minutes in the captured shift definition.");

            migrationBuilder.AddColumn<string>(
                name: "oee_shift_code",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "MasterData shift code captured with its report-time definition.");

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
                comment: "Local shift end time captured from MasterData at report time.");

            migrationBuilder.AddColumn<int>(
                name: "oee_shift_paid_minutes",
                schema: "mes",
                table: "production_reports",
                type: "integer",
                nullable: true,
                comment: "Paid minutes in the captured shift definition.");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "oee_shift_starts_at",
                schema: "mes",
                table: "production_reports",
                type: "time without time zone",
                nullable: true,
                comment: "Local shift start time captured from MasterData at report time.");

            migrationBuilder.AddColumn<string>(
                name: "oee_site_code",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Authoritative MasterData site code captured with the production report.");

            migrationBuilder.AddColumn<string>(
                name: "oee_site_timezone",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "IANA site timezone captured from MasterData at report time.");

            migrationBuilder.AddColumn<string>(
                name: "oee_workshop_code",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Authoritative MasterData workshop code captured with the production report.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "oee_dimension_degraded_reason",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "oee_dimension_resolution_status",
                schema: "mes",
                table: "production_reports");

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
