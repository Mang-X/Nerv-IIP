using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSchedulingAssignedMinuteComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "makespan_minutes",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                comment: "Minutes between the earliest resource occupancy start and latest assignment end.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Minutes between the earliest assignment start and latest assignment end.");

            migrationBuilder.AlterColumn<int>(
                name: "assigned_minutes",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                comment: "Total resource occupied minutes, including operation processing plus setup/changeover time.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Total assigned operation duration in minutes.");

            migrationBuilder.AlterColumn<int>(
                name: "assigned_minutes",
                schema: "scheduling",
                table: "schedule_plan_resource_loads",
                type: "integer",
                nullable: false,
                comment: "Resource occupied minutes in the window, including processing plus setup/changeover time.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Assigned production minutes in the window.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "makespan_minutes",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                comment: "Minutes between the earliest assignment start and latest assignment end.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Minutes between the earliest resource occupancy start and latest assignment end.");

            migrationBuilder.AlterColumn<int>(
                name: "assigned_minutes",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                comment: "Total assigned operation duration in minutes.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Total resource occupied minutes, including operation processing plus setup/changeover time.");

            migrationBuilder.AlterColumn<int>(
                name: "assigned_minutes",
                schema: "scheduling",
                table: "schedule_plan_resource_loads",
                type: "integer",
                nullable: false,
                comment: "Assigned production minutes in the window.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Resource occupied minutes in the window, including processing plus setup/changeover time.");
        }
    }
}
