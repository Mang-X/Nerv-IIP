using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesIssue557ExecutionFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropUniqueConstraint(
                name: "AK_production_reports_organization_id_environment_id_report_no",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.AddColumn<long>(
                name: "labor_time_ticks",
                schema: "mes",
                table: "operation_tasks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Actual labor time stored as .NET ticks after paused duration deduction.");

            migrationBuilder.AddColumn<long>(
                name: "machine_time_ticks",
                schema: "mes",
                table: "operation_tasks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Actual machine time stored as .NET ticks after paused duration deduction.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paused_at_utc",
                schema: "mes",
                table: "operation_tasks",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the operation entered its current paused interval, if any.");

            migrationBuilder.AddColumn<long>(
                name: "paused_duration_ticks",
                schema: "mes",
                table: "operation_tasks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Accumulated paused duration stored as .NET ticks and excluded from actual work time.");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_production_reports_scope_report_no",
                schema: "mes",
                table: "production_reports",
                columns: new[] { "organization_id", "environment_id", "report_no" });

            migrationBuilder.CreateTable(
                name: "output_lot_genealogies",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Output lot genealogy aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the MES genealogy context."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES business work order id that produced the lot."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES output operation task id that produced the lot."),
                    report_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES production report number that created the output lot breakpoint."),
                    produced_lot_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Produced finished-goods lot number assigned by MES."),
                    serial_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional produced serial number assigned by MES."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Good quantity represented by this output lot breakpoint."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the output lot genealogy breakpoint was recorded.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_output_lot_genealogies", x => x.id);
                    table.ForeignKey(
                        name: "fk_output_lot_genealogies_operation_tasks",
                        columns: x => new { x.organization_id, x.environment_id, x.operation_task_id },
                        principalSchema: "mes",
                        principalTable: "operation_tasks",
                        principalColumns: new[] { "organization_id", "environment_id", "operation_task_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_output_lot_genealogies_reports",
                        columns: x => new { x.organization_id, x.environment_id, x.report_no },
                        principalSchema: "mes",
                        principalTable: "production_reports",
                        principalColumns: new[] { "organization_id", "environment_id", "report_no" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_output_lot_genealogies_work_orders",
                        columns: x => new { x.organization_id, x.environment_id, x.work_order_id },
                        principalSchema: "mes",
                        principalTable: "work_orders",
                        principalColumns: new[] { "organization_id", "environment_id", "work_order_id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "MES output lot genealogy breakpoints linking reported finished-goods lots to work orders, operations, reports, and consumed material facts.");

            migrationBuilder.CreateIndex(
                name: "ix_output_lot_genealogies_scope_operation",
                schema: "mes",
                table: "output_lot_genealogies",
                columns: new[] { "organization_id", "environment_id", "operation_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_output_lot_genealogies_scope_work_order",
                schema: "mes",
                table: "output_lot_genealogies",
                columns: new[] { "organization_id", "environment_id", "work_order_id" });

            migrationBuilder.CreateIndex(
                name: "ux_output_lot_genealogies_scope_lot",
                schema: "mes",
                table: "output_lot_genealogies",
                columns: new[] { "organization_id", "environment_id", "produced_lot_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_output_lot_genealogies_scope_report",
                schema: "mes",
                table: "output_lot_genealogies",
                columns: new[] { "organization_id", "environment_id", "report_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "output_lot_genealogies",
                schema: "mes");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_production_reports_scope_report_no",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "labor_time_ticks",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "machine_time_ticks",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "paused_at_utc",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "paused_duration_ticks",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_production_reports_organization_id_environment_id_report_no",
                schema: "mes",
                table: "production_reports",
                columns: new[] { "organization_id", "environment_id", "report_no" });
        }
    }
}
