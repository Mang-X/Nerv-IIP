using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMpsLifecycleAndMrpInputSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "input_coverage_end",
                schema: "demand_planning",
                table: "mrp_runs",
                type: "date",
                nullable: true,
                comment: "Latest input demand date included in this run.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "input_coverage_start",
                schema: "demand_planning",
                table: "mrp_runs",
                type: "date",
                nullable: true,
                comment: "Earliest input demand date included in this run.");

            migrationBuilder.AddColumn<string>(
                name: "input_source_summary",
                schema: "demand_planning",
                table: "mrp_runs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                comment: "Semicolon-separated MRP input source types included in this run.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at_utc",
                schema: "demand_planning",
                table: "master_production_schedules",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                comment: "UTC timestamp when the MPS bucket was created.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "released_at_utc",
                schema: "demand_planning",
                table: "master_production_schedules",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC timestamp when the MPS bucket was released.");

            migrationBuilder.AddColumn<string>(
                name: "released_by",
                schema: "demand_planning",
                table: "master_production_schedules",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Planner or manager that released the MPS bucket.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at_utc",
                schema: "demand_planning",
                table: "master_production_schedules",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC timestamp when the MPS bucket was reviewed.");

            migrationBuilder.AddColumn<string>(
                name: "reviewed_by",
                schema: "demand_planning",
                table: "master_production_schedules",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Planner that reviewed the MPS bucket.");

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "demand_planning",
                table: "master_production_schedules",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Draft",
                comment: "MPS bucket lifecycle status.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at_utc",
                schema: "demand_planning",
                table: "master_production_schedules",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                comment: "UTC timestamp when the MPS bucket was last updated.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "input_coverage_end",
                schema: "demand_planning",
                table: "mrp_runs");

            migrationBuilder.DropColumn(
                name: "input_coverage_start",
                schema: "demand_planning",
                table: "mrp_runs");

            migrationBuilder.DropColumn(
                name: "input_source_summary",
                schema: "demand_planning",
                table: "mrp_runs");

            migrationBuilder.DropColumn(
                name: "created_at_utc",
                schema: "demand_planning",
                table: "master_production_schedules");

            migrationBuilder.DropColumn(
                name: "released_at_utc",
                schema: "demand_planning",
                table: "master_production_schedules");

            migrationBuilder.DropColumn(
                name: "released_by",
                schema: "demand_planning",
                table: "master_production_schedules");

            migrationBuilder.DropColumn(
                name: "reviewed_at_utc",
                schema: "demand_planning",
                table: "master_production_schedules");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                schema: "demand_planning",
                table: "master_production_schedules");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "demand_planning",
                table: "master_production_schedules");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                schema: "demand_planning",
                table: "master_production_schedules");
        }
    }
}
