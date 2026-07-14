using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingOperationOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "problem_json",
                schema: "scheduling",
                table: "schedule_problems",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}",
                comment: "Normalized scheduling problem payload used to validate later operation locks and overrides.");

            migrationBuilder.AddColumn<int>(
                name: "locked_operation_count",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Number of fixed locked operations retained by this plan.");

            migrationBuilder.AddColumn<int>(
                name: "optimizable_operation_count",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Number of non-locked scheduled operations included in optimization KPIs.");

            migrationBuilder.AlterColumn<int>(
                name: "total_tardiness_minutes",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                comment: "Total minutes by which non-locked assigned operations finish after their due dates.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Total minutes by which assigned operations finish after their due dates.");

            migrationBuilder.AlterColumn<int>(
                name: "late_operation_count",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                comment: "Number of non-locked assigned operations finishing after their due dates.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Number of assigned operations finishing after their due dates.");

            migrationBuilder.AlterColumn<decimal>(
                name: "on_time_rate",
                schema: "scheduling",
                table: "schedule_plans",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                comment: "Non-locked operations completed on or before due date divided by non-locked assigned operations.",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldComment: "Assigned operations completed on or before due date divided by assigned operations.");

            migrationBuilder.CreateTable(
                name: "schedule_operation_overrides",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Override row id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Business environment id."),
                    work_order_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Real work-order public id."),
                    operation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Real operation public id."),
                    operation_sequence = table.Column<int>(type: "integer", nullable: false, comment: "Operation sequence within the work order."),
                    resource_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Fixed executable resource id."),
                    work_center_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Work center owning the fixed resource."),
                    start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Fixed start timestamp in UTC."),
                    end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Fixed end timestamp in UTC."),
                    lock_reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Explainable lock reason code."),
                    source_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Scheduling API or MES dispatch source type."),
                    source_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Optional source integration event id."),
                    actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Actor that created the current fact."),
                    source_occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Source ordering timestamp used to reject stale updates."),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Last persistence update timestamp in UTC.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_operation_overrides", x => x.id);
                },
                comment: "Current fixed operation assignments created by manual Scheduling adjustments or MES dispatch.");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_operation_overrides_organization_id_environment_id~",
                schema: "scheduling",
                table: "schedule_operation_overrides",
                columns: new[] { "organization_id", "environment_id", "operation_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schedule_operation_overrides",
                schema: "scheduling");

            migrationBuilder.DropColumn(
                name: "problem_json",
                schema: "scheduling",
                table: "schedule_problems");

            migrationBuilder.DropColumn(
                name: "locked_operation_count",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "optimizable_operation_count",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.AlterColumn<int>(
                name: "total_tardiness_minutes",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                comment: "Total minutes by which assigned operations finish after their due dates.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Total minutes by which non-locked assigned operations finish after their due dates.");

            migrationBuilder.AlterColumn<int>(
                name: "late_operation_count",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                comment: "Number of assigned operations finishing after their due dates.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Number of non-locked assigned operations finishing after their due dates.");

            migrationBuilder.AlterColumn<decimal>(
                name: "on_time_rate",
                schema: "scheduling",
                table: "schedule_plans",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                comment: "Assigned operations completed on or before due date divided by assigned operations.",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldComment: "Non-locked operations completed on or before due date divided by non-locked assigned operations.");
        }
    }
}
