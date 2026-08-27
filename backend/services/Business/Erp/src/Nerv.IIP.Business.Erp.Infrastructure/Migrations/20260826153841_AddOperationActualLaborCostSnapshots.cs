using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationActualLaborCostSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "labor_basis",
                schema: "erp",
                table: "work_order_cost_details",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Labor costing basis: theoretical report, actual operation, or append-only reversal/replacement.");

            migrationBuilder.AddColumn<string>(
                name: "labor_lineage_id",
                schema: "erp",
                table: "work_order_cost_details",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Stable MES report or operation-settlement lineage for auditable labor replacement and reversal.");

            migrationBuilder.CreateTable(
                name: "operation_labor_covered_reports",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Covered production-report lineage id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization boundary."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment boundary."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work-order public identifier."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES operation-task public identifier."),
                    settlement_revision = table.Column<long>(type: "bigint", nullable: false, comment: "MES settlement revision that covered the report."),
                    report_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES production report number permanently suppressed from theoretical labor costing.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_labor_covered_reports", x => x.id);
                },
                comment: "Permanent MES report lineage covered by one operation-level actual labor settlement.");

            migrationBuilder.CreateTable(
                name: "operation_labor_settlement_states",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Operation labor settlement state id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization boundary."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment boundary."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES operation-task public identifier."),
                    highest_revision = table.Column<long>(type: "bigint", nullable: false, comment: "Highest settlement or void revision observed for this operation."),
                    active_revision = table.Column<long>(type: "bigint", nullable: true, comment: "Currently active settlement revision, or null after a void.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_labor_settlement_states", x => x.id);
                },
                comment: "Monotonic ERP processing watermark and active revision for one MES operation task.");

            migrationBuilder.CreateTable(
                name: "operation_labor_settlement_voids",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Operation labor settlement void id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization boundary."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment boundary."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work-order public identifier."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES operation-task public identifier."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Frozen MES work-center public identifier."),
                    settlement_revision = table.Column<long>(type: "bigint", nullable: false, comment: "Voided MES settlement revision."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Original MES completion instant."),
                    voided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "MES void occurrence instant."),
                    actual_labor_ticks = table.Column<long>(type: "bigint", nullable: false, comment: "Exact copy of original actual labor ticks."),
                    actual_labor_hours = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: false, comment: "Exact copy of original actual labor hours."),
                    work_center_cost_rate_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Exact copy of original frozen rate id."),
                    rate_revision = table.Column<int>(type: "integer", nullable: false, comment: "Exact copy of original frozen rate revision."),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, comment: "Exact copy of original frozen currency."),
                    hourly_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Exact copy of original frozen hourly rate."),
                    rate_basis_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Exact copy of original rate basis instant."),
                    rate_basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Exact copy of original rate basis."),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Strictly opposite amount of the original settlement."),
                    source_event_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "MES settlement-void event id."),
                    payload_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false, comment: "SHA-256 of canonical void business payload fields.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_labor_settlement_voids", x => x.id);
                    table.ForeignKey(
                        name: "FK_operation_labor_settlement_voids_work_center_cost_rates_wor~",
                        column: x => x.work_center_cost_rate_id,
                        principalSchema: "erp",
                        principalTable: "work_center_cost_rates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Append-only exact reversal of an immutable operation labor settlement snapshot.");

            migrationBuilder.CreateTable(
                name: "operation_labor_settlements",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Operation labor settlement snapshot id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization boundary."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment boundary."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work-order public identifier."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES operation-task public identifier."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work-center public identifier used for rate selection."),
                    settlement_revision = table.Column<long>(type: "bigint", nullable: false, comment: "Monotonic MES actual-time settlement revision."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "MES operation completion instant used as the rate basis."),
                    actual_labor_ticks = table.Column<long>(type: "bigint", nullable: false, comment: "Lossless MES actual labor duration in TimeSpan ticks."),
                    actual_labor_hours = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: false, comment: "Actual labor hours derived from the frozen tick value."),
                    work_center_cost_rate_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Frozen work-center standard labor-rate revision id."),
                    rate_revision = table.Column<int>(type: "integer", nullable: false, comment: "Frozen work-center standard labor-rate revision number."),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, comment: "Frozen three-letter currency code; no implicit conversion is allowed."),
                    hourly_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Frozen standard labor hourly rate."),
                    rate_basis_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC rate basis equal to the MES completion instant."),
                    rate_basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Frozen rate basis; currently standard."),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Actual labor hours multiplied by the frozen standard rate."),
                    source_event_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "First MES event id that established the immutable snapshot."),
                    payload_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false, comment: "SHA-256 of canonical business payload fields for conflict detection.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_labor_settlements", x => x.id);
                    table.ForeignKey(
                        name: "FK_operation_labor_settlements_work_center_cost_rates_work_cen~",
                        column: x => x.work_center_cost_rate_id,
                        principalSchema: "erp",
                        principalTable: "work_center_cost_rates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Immutable ERP actual-operation labor snapshot priced at a frozen standard work-center rate.");

            migrationBuilder.CreateIndex(
                name: "ix_operation_labor_covered_reports_settlement",
                schema: "erp",
                table: "operation_labor_covered_reports",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "settlement_revision" });

            migrationBuilder.CreateIndex(
                name: "ux_operation_labor_covered_reports_report",
                schema: "erp",
                table: "operation_labor_covered_reports",
                columns: new[] { "organization_id", "environment_id", "report_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_operation_labor_settlement_states_operation",
                schema: "erp",
                table: "operation_labor_settlement_states",
                columns: new[] { "organization_id", "environment_id", "operation_task_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operation_labor_settlement_voids_work_center_cost_rate_id",
                schema: "erp",
                table: "operation_labor_settlement_voids",
                column: "work_center_cost_rate_id");

            migrationBuilder.CreateIndex(
                name: "ux_operation_labor_settlement_voids_business_identity",
                schema: "erp",
                table: "operation_labor_settlement_voids",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "settlement_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operation_labor_settlements_work_center_cost_rate_id",
                schema: "erp",
                table: "operation_labor_settlements",
                column: "work_center_cost_rate_id");

            migrationBuilder.CreateIndex(
                name: "ix_operation_labor_settlements_work_order",
                schema: "erp",
                table: "operation_labor_settlements",
                columns: new[] { "organization_id", "environment_id", "work_order_id" });

            migrationBuilder.CreateIndex(
                name: "ux_operation_labor_settlements_business_identity",
                schema: "erp",
                table: "operation_labor_settlements",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "settlement_revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operation_labor_covered_reports",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "operation_labor_settlement_states",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "operation_labor_settlement_voids",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "operation_labor_settlements",
                schema: "erp");

            migrationBuilder.DropColumn(
                name: "labor_basis",
                schema: "erp",
                table: "work_order_cost_details");

            migrationBuilder.DropColumn(
                name: "labor_lineage_id",
                schema: "erp",
                table: "work_order_cost_details");
        }
    }
}
