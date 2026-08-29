using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderLaborVarianceReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operation_labor_report_snapshots",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Operation labor report snapshot id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization boundary."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment boundary."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work-order public identifier."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES operation-task public identifier."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Frozen MES work-center identifier."),
                    report_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES production-report business identifier."),
                    good_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Frozen reported good quantity before reversal sign normalization."),
                    scrap_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Frozen reported scrap quantity; excluded from standard labor hours."),
                    rework_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Frozen reported rework quantity; excluded from standard labor hours."),
                    uom_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Frozen MES output unit of measure."),
                    theoretical_rate_per_hour = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true, comment: "Frozen theoretical good-output rate per labor hour."),
                    reported_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Original MES production-report UTC timestamp."),
                    is_reversal = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this report reverses a prior production report."),
                    reversed_report_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Original MES report number for a reversal snapshot."),
                    source_event_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "MES event id that established this immutable snapshot.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_labor_report_snapshots", x => x.id);
                },
                comment: "ERP immutable MES production-report basis used for standard labor and efficiency variance.");

            migrationBuilder.CreateIndex(
                name: "ix_operation_labor_report_snapshots_work_order_operation",
                schema: "erp",
                table: "operation_labor_report_snapshots",
                columns: new[] { "organization_id", "environment_id", "work_order_id", "operation_task_id" });

            migrationBuilder.CreateIndex(
                name: "ux_operation_labor_report_snapshots_scope_report",
                schema: "erp",
                table: "operation_labor_report_snapshots",
                columns: new[] { "organization_id", "environment_id", "report_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operation_labor_report_snapshots",
                schema: "erp");
        }
    }
}
