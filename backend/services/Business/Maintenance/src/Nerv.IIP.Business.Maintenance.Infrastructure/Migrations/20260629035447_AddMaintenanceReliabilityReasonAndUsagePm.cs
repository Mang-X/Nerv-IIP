using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceReliabilityReasonAndUsagePm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "failure_cause_code",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Structured failure cause code captured from alarm or inspection context.");

            migrationBuilder.AddColumn<string>(
                name: "failure_mode_code",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Structured failure mode code captured from alarm or inspection context.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "repair_started_at_utc",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when effective repair work started.");

            migrationBuilder.AddColumn<decimal>(
                name: "last_generated_runtime_hours",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Last cumulative runtime-hour reading used to generate a PM work order.");

            migrationBuilder.AddColumn<decimal>(
                name: "next_due_runtime_hours",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Next cumulative runtime-hour threshold for usage-triggered PM generation.");

            migrationBuilder.AddColumn<decimal>(
                name: "runtime_hour_interval",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Optional runtime-hour interval for usage-triggered preventive maintenance.");

            migrationBuilder.AddColumn<string>(
                name: "loss_category",
                schema: "maintenance",
                table: "downtime_reasons",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "TPM six-big-loss or equivalent OEE loss classification.");

            migrationBuilder.AddColumn<string>(
                name: "reason_category",
                schema: "maintenance",
                table: "downtime_reasons",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Reason category for maintenance RCA and reporting.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "failure_cause_code",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "failure_mode_code",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "repair_started_at_utc",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "last_generated_runtime_hours",
                schema: "maintenance",
                table: "maintenance_plans");

            migrationBuilder.DropColumn(
                name: "next_due_runtime_hours",
                schema: "maintenance",
                table: "maintenance_plans");

            migrationBuilder.DropColumn(
                name: "runtime_hour_interval",
                schema: "maintenance",
                table: "maintenance_plans");

            migrationBuilder.DropColumn(
                name: "loss_category",
                schema: "maintenance",
                table: "downtime_reasons");

            migrationBuilder.DropColumn(
                name: "reason_category",
                schema: "maintenance",
                table: "downtime_reasons");
        }
    }
}
