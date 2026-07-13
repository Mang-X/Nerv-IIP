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

            // Explicit legacy-unknown strategy: existing rows keep scheduled_at_utc = NULL and are NOT backfilled.
            // The pre-migration schema has no reliable way to tell an APS placement from a manual dispatch —
            // assigned_at_utc is written by both ApplyScheduleAssignment (schedule time) and Assign (dispatch time,
            // which also overwrites it), Assign can be called with a null operator (so assigned_user_id IS NULL is
            // not APS-specific), and ApplyScheduleAssignment does not clear a prior assigned_user_id (so a
            // dispatch→reschedule row keeps a user). Any assigned_at_utc-based backfill would therefore both
            // mislabel manual dispatches as scheduled and miss genuinely-scheduled dispatched rows. Rather than
            // guess wrong, legacy rows are treated as "no recorded APS placement" (rendered 未排程) and reconciled
            // to their true schedule state the next time a released schedule places them (ApplyScheduleAssignment
            // sets scheduled_at_utc) or the schedule stream is replayed. The forward guarantee — only
            // ApplyScheduleAssignment, never manual dispatch, writes scheduled_at_utc — is covered by
            // OperationTaskScheduledAtTests.
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
