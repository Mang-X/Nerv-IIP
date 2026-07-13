using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationTaskScheduleInvalidationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "schedule_invalidation_reason_code",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "Latest scheduling invalidation reason code when the task is schedule invalidated; cleared when a released schedule re-plans the task.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "scheduled_at_utc",
                schema: "mes",
                table: "operation_tasks",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when a released APS schedule last placed this task; set only by schedule assignment (not manual dispatch) and used to derive the 已排程/未排程 schedule state.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "schedule_invalidation_reason_code",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "scheduled_at_utc",
                schema: "mes",
                table: "operation_tasks");
        }
    }
}
