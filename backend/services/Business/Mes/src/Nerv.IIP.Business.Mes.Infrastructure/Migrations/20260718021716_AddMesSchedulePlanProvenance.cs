using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesSchedulePlanProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "schedule_plan_id",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Scheduling plan id that currently owns this task's APS assignment, or null after matching revocation.");

            migrationBuilder.AddColumn<long>(
                name: "schedule_release_revision",
                schema: "mes",
                table: "operation_tasks",
                type: "bigint",
                nullable: true,
                comment: "Monotonic Scheduling release revision that currently owns this task's APS assignment.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "schedule_plan_id",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "schedule_release_revision",
                schema: "mes",
                table: "operation_tasks");
        }
    }
}
