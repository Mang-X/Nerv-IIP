using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesOperationActualTimeSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "actual_time_settlement_revision", schema: "mes", table: "operation_tasks",
                type: "bigint", nullable: false, defaultValue: 0L,
                comment: "Monotonic MES actual-time settlement revision; zero means the operation has never emitted a settlement.");

            migrationBuilder.AddColumn<int>(
                name: "row_version", schema: "mes", table: "operation_tasks",
                type: "integer", nullable: false, defaultValue: 0,
                comment: "Optimistic row version.");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_production_reports_scope_report_task",
                schema: "mes",
                table: "production_reports",
                columns: new[] { "organization_id", "environment_id", "report_no", "work_order_id", "operation_task_id" });

            migrationBuilder.CreateTable(
                name: "operation_actual_time_settlements",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Actual-time settlement revision identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the settlement."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work order id frozen by the settlement."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES operation task id frozen by the settlement."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work center id frozen by the settlement."),
                    revision = table.Column<long>(type: "bigint", nullable: false, comment: "Positive monotonic actual-time settlement business revision."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Operation completion time in UTC frozen by this settlement."),
                    actual_labor_ticks = table.Column<long>(type: "bigint", nullable: false, comment: "Nonnegative actual labor duration in .NET ticks frozen by this settlement."),
                    actual_machine_ticks = table.Column<long>(type: "bigint", nullable: false, comment: "Nonnegative actual machine duration in .NET ticks frozen by this settlement."),
                    voided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the settlement was voided by completion-report reversal; null while active.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_actual_time_settlements", x => x.id);
                    table.UniqueConstraint("ak_operation_actual_time_settlements_id_scope_task", x => new { x.id, x.organization_id, x.environment_id, x.work_order_id, x.operation_task_id });
                    table.CheckConstraint("ck_operation_actual_time_settlements_revision_positive", "revision > 0");
                    table.CheckConstraint("ck_operation_actual_time_settlements_ticks_nonnegative", "actual_labor_ticks >= 0 AND actual_machine_ticks >= 0");
                    table.CheckConstraint("ck_operation_actual_time_settlements_void_order", "voided_at_utc IS NULL OR voided_at_utc >= completed_at_utc");
                    table.ForeignKey(
                        name: "fk_operation_actual_time_settlements_operation_tasks",
                        columns: x => new { x.organization_id, x.environment_id, x.operation_task_id, x.work_order_id },
                        principalSchema: "mes", principalTable: "operation_tasks",
                        principalColumns: new[] { "organization_id", "environment_id", "operation_task_id", "work_order_id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Immutable MES operation actual-time settlement revisions and their void lifecycle.");

            migrationBuilder.CreateTable(
                name: "operation_actual_time_settlement_reports",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Settlement-to-report lineage identifier."),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning actual-time settlement revision identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id copied for report foreign-key isolation."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id copied for report foreign-key isolation."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work order id copied to enforce report ownership."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES operation task id copied to enforce report ownership."),
                    report_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Covered MES production report number.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_actual_time_settlement_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_operation_actual_time_settlement_reports_production_reports",
                        columns: x => new { x.organization_id, x.environment_id, x.report_no, x.work_order_id, x.operation_task_id },
                        principalSchema: "mes", principalTable: "production_reports",
                        principalColumns: new[] { "organization_id", "environment_id", "report_no", "work_order_id", "operation_task_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_operation_actual_time_settlement_reports_settlement",
                        columns: x => new { x.settlement_id, x.organization_id, x.environment_id, x.work_order_id, x.operation_task_id },
                        principalSchema: "mes", principalTable: "operation_actual_time_settlements",
                        principalColumns: new[] { "id", "organization_id", "environment_id", "work_order_id", "operation_task_id" },
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Relational production-report lineage covered by one MES actual-time settlement revision.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_tasks_actual_time_settlement_revision_nonnegative",
                schema: "mes", table: "operation_tasks", sql: "actual_time_settlement_revision >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_operation_actual_time_settlement_reports_report_owner",
                schema: "mes", table: "operation_actual_time_settlement_reports",
                columns: new[] { "organization_id", "environment_id", "report_no", "work_order_id", "operation_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_operation_actual_time_settlement_reports_settlement_owner",
                schema: "mes", table: "operation_actual_time_settlement_reports",
                columns: new[] { "settlement_id", "organization_id", "environment_id", "work_order_id", "operation_task_id" });

            migrationBuilder.CreateIndex(
                name: "ux_operation_actual_time_settlement_reports_settlement_report",
                schema: "mes", table: "operation_actual_time_settlement_reports",
                columns: new[] { "settlement_id", "report_no" }, unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_operation_actual_time_settlements_scope_task",
                schema: "mes", table: "operation_actual_time_settlements",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "work_order_id" });

            migrationBuilder.CreateIndex(
                name: "ux_operation_actual_time_settlements_scope_task_revision",
                schema: "mes", table: "operation_actual_time_settlements",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "revision" }, unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "operation_actual_time_settlement_reports", schema: "mes");
            migrationBuilder.DropTable(name: "operation_actual_time_settlements", schema: "mes");
            migrationBuilder.DropUniqueConstraint(
                name: "ak_production_reports_scope_report_task",
                schema: "mes",
                table: "production_reports");
            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_tasks_actual_time_settlement_revision_nonnegative",
                schema: "mes", table: "operation_tasks");
            migrationBuilder.DropColumn(name: "actual_time_settlement_revision", schema: "mes", table: "operation_tasks");
            migrationBuilder.DropColumn(name: "row_version", schema: "mes", table: "operation_tasks");
        }
    }
}
