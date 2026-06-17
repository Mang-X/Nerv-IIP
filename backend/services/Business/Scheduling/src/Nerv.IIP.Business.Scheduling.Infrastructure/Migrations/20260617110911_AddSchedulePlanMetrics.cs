using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulePlanMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "assigned_minutes",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Total assigned operation duration in minutes.");

            migrationBuilder.AddColumn<decimal>(
                name: "average_resource_utilization",
                schema: "scheduling",
                table: "schedule_plans",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Total assigned minutes divided by total available minutes across resource load windows.");

            migrationBuilder.AddColumn<int>(
                name: "late_operation_count",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Number of assigned operations finishing after their due dates.");

            migrationBuilder.AddColumn<int>(
                name: "makespan_minutes",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Minutes between the earliest assignment start and latest assignment end.");

            migrationBuilder.AddColumn<decimal>(
                name: "on_time_rate",
                schema: "scheduling",
                table: "schedule_plans",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Assigned operations completed on or before due date divided by assigned operations.");

            migrationBuilder.AddColumn<int>(
                name: "scheduled_operation_count",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Number of operations assigned by this plan.");

            migrationBuilder.AddColumn<int>(
                name: "total_tardiness_minutes",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Total minutes by which assigned operations finish after their due dates.");

            migrationBuilder.AddColumn<int>(
                name: "unscheduled_operation_count",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Number of operations left unscheduled by this plan.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "assigned_minutes",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "average_resource_utilization",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "late_operation_count",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "makespan_minutes",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "on_time_rate",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "scheduled_operation_count",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "total_tardiness_minutes",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "unscheduled_operation_count",
                schema: "scheduling",
                table: "schedule_plans");
        }
    }
}
