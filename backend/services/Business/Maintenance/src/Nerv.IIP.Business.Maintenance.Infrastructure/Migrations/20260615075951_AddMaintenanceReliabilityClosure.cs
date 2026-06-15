using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceReliabilityClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "alarm_cleared",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether the source IndustrialTelemetry alarm has been cleared while awaiting maintenance confirmation.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "alarm_cleared_at_utc",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the source alarm was cleared.");

            migrationBuilder.AddColumn<string>(
                name: "source_plan_code",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Maintenance plan code that generated this work order, when applicable.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_generated_on",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "date",
                nullable: true,
                comment: "Last business date for which the plan generated a maintenance work order.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "next_due_on",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "date",
                nullable: true,
                comment: "Next business date on which the preventive maintenance plan is due.");

            migrationBuilder.Sql("""
                UPDATE maintenance.maintenance_plans
                SET next_due_on = starts_on
                WHERE next_due_on IS NULL;
                """);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "next_due_on",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "date",
                nullable: false,
                comment: "Next business date on which the preventive maintenance plan is due.",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true,
                oldComment: "Next business date on which the preventive maintenance plan is due.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "alarm_cleared",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "alarm_cleared_at_utc",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "source_plan_code",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "last_generated_on",
                schema: "maintenance",
                table: "maintenance_plans");

            migrationBuilder.DropColumn(
                name: "next_due_on",
                schema: "maintenance",
                table: "maintenance_plans");
        }
    }
}
