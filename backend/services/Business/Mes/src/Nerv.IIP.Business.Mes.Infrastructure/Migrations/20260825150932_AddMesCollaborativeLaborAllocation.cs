using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesCollaborativeLaborAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operation_task_participants",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Operation task participant fact id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant scope."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment scope."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES operation task public id."),
                    worker_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData worker user id captured for collaboration."),
                    worker_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "Worker display name snapshot resolved at dispatch time."),
                    share_percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false, comment: "Worker share of the operation labor time in percent.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_task_participants", x => x.id);
                    table.CheckConstraint("ck_operation_task_participants_share_percent", "share_percent > 0 AND share_percent <= 100");
                    table.ForeignKey(
                        name: "fk_operation_task_participants_operation_tasks",
                        columns: x => new { x.organization_id, x.environment_id, x.operation_task_id },
                        principalSchema: "mes",
                        principalTable: "operation_tasks",
                        principalColumns: new[] { "organization_id", "environment_id", "operation_task_id" },
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Current MES operation collaboration roster with worker identity snapshots and labor shares.");

            migrationBuilder.CreateTable(
                name: "production_report_labor_allocations",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Production report labor allocation id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant scope."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment scope."),
                    report_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Completing production report number."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work order public id."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES operation task public id."),
                    worker_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Allocated MasterData worker user id snapshot."),
                    worker_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "Allocated worker display name snapshot."),
                    share_percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false, comment: "Worker labor share captured when the operation completed."),
                    allocated_labor_ticks = table.Column<long>(type: "bigint", nullable: false, comment: "Final operation labor ticks allocated to this worker.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_report_labor_allocations", x => x.id);
                    table.CheckConstraint("ck_production_report_labor_allocations_share_percent", "share_percent > 0 AND share_percent <= 100");
                    table.CheckConstraint("ck_production_report_labor_allocations_ticks", "allocated_labor_ticks >= 0");
                    table.ForeignKey(
                        name: "fk_production_report_labor_allocations_operation_tasks",
                        columns: x => new { x.organization_id, x.environment_id, x.operation_task_id },
                        principalSchema: "mes",
                        principalTable: "operation_tasks",
                        principalColumns: new[] { "organization_id", "environment_id", "operation_task_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_production_report_labor_allocations_reports",
                        columns: x => new { x.organization_id, x.environment_id, x.report_no },
                        principalSchema: "mes",
                        principalTable: "production_reports",
                        principalColumns: new[] { "organization_id", "environment_id", "report_no" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_production_report_labor_allocations_work_orders",
                        columns: x => new { x.organization_id, x.environment_id, x.work_order_id },
                        principalSchema: "mes",
                        principalTable: "work_orders",
                        principalColumns: new[] { "organization_id", "environment_id", "work_order_id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Immutable worker labor allocation snapshots created by completing MES production reports.");

            migrationBuilder.CreateIndex(
                name: "ux_operation_task_participants_scope_task_worker",
                schema: "mes",
                table: "operation_task_participants",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "worker_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_production_report_labor_allocations_scope_task_worker",
                schema: "mes",
                table: "production_report_labor_allocations",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "worker_id" });

            migrationBuilder.CreateIndex(
                name: "ix_production_report_labor_allocations_scope_work_order",
                schema: "mes",
                table: "production_report_labor_allocations",
                columns: new[] { "organization_id", "environment_id", "work_order_id" });

            migrationBuilder.CreateIndex(
                name: "ux_production_report_labor_allocations_scope_report_worker",
                schema: "mes",
                table: "production_report_labor_allocations",
                columns: new[] { "organization_id", "environment_id", "report_no", "worker_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operation_task_participants",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "production_report_labor_allocations",
                schema: "mes");
        }
    }
}
