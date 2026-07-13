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

            // Backfill scheduled_at_utc for existing rows that a released APS schedule had already placed, so
            // they don't regress to "未排程" after upgrade. Only ApplyScheduleAssignment writes assigned_at_utc
            // without an operator (assigned_user_id), while manual dispatch (Assign) always sets assigned_user_id
            // and overwrites assigned_at_utc with the dispatch time. So `assigned_at_utc IS NOT NULL AND
            // assigned_user_id IS NULL` selects exactly the APS-scheduled-not-yet-dispatched rows where
            // assigned_at_utc is the schedule time — an auditable, non-lossy backfill. Rows already dispatched
            // carry a dispatch timestamp (not a schedule time) and no recoverable schedule instant, so they are
            // intentionally left null and re-populated by the next released schedule / event replay rather than
            // copied from the ambiguous assigned_at_utc.
            migrationBuilder.Sql(
                "UPDATE mes.operation_tasks SET scheduled_at_utc = assigned_at_utc " +
                "WHERE assigned_at_utc IS NOT NULL AND assigned_user_id IS NULL;");
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
