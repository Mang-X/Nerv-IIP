using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeMaintenancePlanTriggersOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateOnly>(
                name: "next_due_on",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "date",
                nullable: true,
                comment: "Next business date on which the calendar-triggered plan is due, or null for a runtime-only plan.",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldComment: "Next business date on which the preventive maintenance plan is due.");

            migrationBuilder.AlterColumn<string>(
                name: "interval",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Calendar interval expression (ISO-8601 P7D) for the calendar trigger, or null for a runtime-only plan.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Explicit maintenance interval expression, for example ISO-8601 P7D.");

            // Defense-in-depth for the nullable columns (the domain layer already enforces both): every plan
            // must keep at least one trigger, and the calendar interval/next_due_on stay paired (both null for
            // a runtime-only plan, both set otherwise) so no "no-trigger" or never-due inconsistent row exists.
            migrationBuilder.AddCheckConstraint(
                name: "ck_maintenance_plans_has_trigger",
                schema: "maintenance",
                table: "maintenance_plans",
                sql: "interval IS NOT NULL OR runtime_hour_interval IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_maintenance_plans_calendar_trigger_paired",
                schema: "maintenance",
                table: "maintenance_plans",
                sql: "(interval IS NULL) = (next_due_on IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the nullable-era check constraints before re-tightening the columns.
            migrationBuilder.DropCheckConstraint(
                name: "ck_maintenance_plans_calendar_trigger_paired",
                schema: "maintenance",
                table: "maintenance_plans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_maintenance_plans_has_trigger",
                schema: "maintenance",
                table: "maintenance_plans");

            // Runtime-only plans created after Up have null interval/next_due_on. Re-adding the NOT NULL
            // constraint would fail on those rows (SET NOT NULL rejects existing nulls) and an empty-string
            // interval would make ConsumeDueDates throw and block generate-due. Backfill to safe, parseable
            // values first (a 100-year calendar interval effectively never fires, so runtime-only plans stay
            // usage-driven); this is a lossy rollback — the pure runtime-only semantics cannot be preserved.
            migrationBuilder.Sql(
                "UPDATE maintenance.maintenance_plans SET interval = 'P36500D' WHERE interval IS NULL;");
            // next_due_on must land far in the future (aligned with the ~100-year fallback interval), NOT at
            // the past starts_on — otherwise the first post-downgrade generate-due would immediately open a
            // spurious date:* work order for every runtime-only plan.
            migrationBuilder.Sql(
                "UPDATE maintenance.maintenance_plans SET next_due_on = GREATEST(starts_on, CURRENT_DATE) + 36500 WHERE next_due_on IS NULL;");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "next_due_on",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                comment: "Next business date on which the preventive maintenance plan is due.",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true,
                oldComment: "Next business date on which the calendar-triggered plan is due, or null for a runtime-only plan.");

            migrationBuilder.AlterColumn<string>(
                name: "interval",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "Explicit maintenance interval expression, for example ISO-8601 P7D.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldComment: "Calendar interval expression (ISO-8601 P7D) for the calendar trigger, or null for a runtime-only plan.");
        }
    }
}
