using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationMachineOverheadSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "machine_overhead_currency_code",
                schema: "erp",
                table: "work_order_costs",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: true,
                comment: "Frozen machine-overhead currency; it must match priced labor when both exist.");

            migrationBuilder.AddColumn<string>(
                name: "machine_overhead_basis",
                schema: "erp",
                table: "work_order_cost_details",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Machine-overhead basis: actual operation, explicit not-applicable, or append-only reversal/supersession.");

            migrationBuilder.AddColumn<string>(
                name: "machine_overhead_lineage_id",
                schema: "erp",
                table: "work_order_cost_details",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Stable MES operation-settlement lineage for machine-overhead audit.");

            migrationBuilder.Sql("""
                COMMENT ON TABLE erp.work_order_cost_details IS 'ERP auditable labor, material, or machine-overhead cost detail.';
                COMMENT ON COLUMN erp.work_order_cost_details.cost_type IS 'Labor, material, or machine-overhead cost type.';
                COMMENT ON COLUMN erp.work_order_cost_details.quantity IS 'Labor or machine hours, or material quantity.';
                COMMENT ON COLUMN erp.work_order_cost_details.rate IS 'Labor or machine-overhead hourly rate, or moving-average material unit cost.';
                """);

            migrationBuilder.CreateTable(
                name: "operation_machine_overhead_settlement_states",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Machine-overhead settlement state id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization boundary."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment boundary."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES operation-task public identifier."),
                    highest_revision = table.Column<long>(type: "bigint", nullable: false, comment: "Highest settlement or void revision observed."),
                    active_revision = table.Column<long>(type: "bigint", nullable: true, comment: "Currently active revision, or null after a void.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_machine_overhead_settlement_states", x => x.id);
                },
                comment: "Monotonic ERP machine-overhead processing watermark and active revision for one MES operation.");

            migrationBuilder.CreateTable(
                name: "operation_machine_overhead_settlement_voids",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Machine-overhead settlement void id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization boundary."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment boundary."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work-order public identifier."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES operation-task public identifier."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work-center public identifier used for rate selection."),
                    settlement_revision = table.Column<long>(type: "bigint", nullable: false, comment: "Monotonic MES actual-time settlement revision."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC completion instant used to select the accounting period."),
                    voided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC MES void occurrence instant."),
                    applicability = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Frozen applicable or explicitly not-applicable ERP rate status."),
                    device_asset_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Authoritative MES execution-device snapshot when machine time is available."),
                    actual_machine_ticks = table.Column<long>(type: "bigint", nullable: true, comment: "Lossless billable machine duration in TimeSpan ticks."),
                    actual_machine_hours = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: true, comment: "Display hours derived from ticks; pricing uses ticks directly."),
                    machine_time_basis_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "MES authoritative machine-time calculation basis."),
                    work_center_machine_overhead_rate_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Frozen monthly rate revision id."),
                    accounting_period_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Frozen accounting period selected by completion instant."),
                    rate_revision = table.Column<int>(type: "integer", nullable: false, comment: "Frozen monthly machine-overhead rate revision number."),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, comment: "Frozen three-letter currency code."),
                    fixed_hourly_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Frozen fixed machine-overhead hourly rate."),
                    variable_hourly_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Frozen variable machine-overhead hourly rate."),
                    fixed_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Fixed machine overhead rounded to six decimals with ToEven."),
                    variable_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Variable machine overhead rounded to six decimals with ToEven."),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Total machine overhead independently rounded to six decimals with ToEven."),
                    source_event_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "First MES V2 event id that established the snapshot."),
                    payload_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false, comment: "SHA-256 of canonical MES machine business fields.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_machine_overhead_settlement_voids", x => x.id);
                    table.ForeignKey(
                        name: "FK_operation_machine_overhead_settlement_voids_work_center_mac~",
                        column: x => x.work_center_machine_overhead_rate_id,
                        principalSchema: "erp",
                        principalTable: "work_center_machine_overhead_rates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Append-only exact reversal of an immutable operation machine-overhead snapshot.");

            migrationBuilder.CreateTable(
                name: "operation_machine_overhead_settlements",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Machine-overhead settlement snapshot id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization boundary."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment boundary."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work-order public identifier."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES operation-task public identifier."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work-center public identifier used for rate selection."),
                    settlement_revision = table.Column<long>(type: "bigint", nullable: false, comment: "Monotonic MES actual-time settlement revision."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC completion instant used to select the accounting period."),
                    applicability = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Frozen applicable or explicitly not-applicable ERP rate status."),
                    device_asset_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Authoritative MES execution-device snapshot when machine time is available."),
                    actual_machine_ticks = table.Column<long>(type: "bigint", nullable: true, comment: "Lossless billable machine duration in TimeSpan ticks."),
                    actual_machine_hours = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: true, comment: "Display hours derived from ticks; pricing uses ticks directly."),
                    machine_time_basis_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "MES authoritative machine-time calculation basis."),
                    work_center_machine_overhead_rate_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Frozen monthly rate revision id."),
                    accounting_period_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Frozen accounting period selected by completion instant."),
                    rate_revision = table.Column<int>(type: "integer", nullable: false, comment: "Frozen monthly machine-overhead rate revision number."),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, comment: "Frozen three-letter currency code."),
                    fixed_hourly_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Frozen fixed machine-overhead hourly rate."),
                    variable_hourly_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Frozen variable machine-overhead hourly rate."),
                    fixed_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Fixed machine overhead rounded to six decimals with ToEven."),
                    variable_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Variable machine overhead rounded to six decimals with ToEven."),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Total machine overhead independently rounded to six decimals with ToEven."),
                    source_event_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "First MES V2 event id that established the snapshot."),
                    payload_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false, comment: "SHA-256 of canonical MES machine business fields.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_machine_overhead_settlements", x => x.id);
                    table.CheckConstraint("ck_operation_machine_overhead_settlements_fact", "(applicability = 'Applicable' AND device_asset_id IS NOT NULL AND actual_machine_ticks >= 0 AND actual_machine_hours IS NOT NULL AND machine_time_basis_code IS NOT NULL) OR (applicability = 'NotApplicable' AND device_asset_id IS NULL AND actual_machine_ticks IS NULL AND actual_machine_hours IS NULL AND machine_time_basis_code IS NULL AND fixed_hourly_rate = 0 AND variable_hourly_rate = 0 AND fixed_amount = 0 AND variable_amount = 0 AND amount = 0)");
                    table.ForeignKey(
                        name: "FK_operation_machine_overhead_settlements_work_center_machine_~",
                        column: x => x.work_center_machine_overhead_rate_id,
                        principalSchema: "erp",
                        principalTable: "work_center_machine_overhead_rates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Immutable ERP machine-overhead snapshot priced from authoritative MES machine time and a frozen monthly rate.");

            migrationBuilder.CreateIndex(
                name: "ux_operation_machine_overhead_settlement_states_operation",
                schema: "erp",
                table: "operation_machine_overhead_settlement_states",
                columns: new[] { "organization_id", "environment_id", "operation_task_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operation_machine_overhead_settlement_voids_work_center_mac~",
                schema: "erp",
                table: "operation_machine_overhead_settlement_voids",
                column: "work_center_machine_overhead_rate_id");

            migrationBuilder.CreateIndex(
                name: "ux_op_machine_overhead_settlement_voids_identity",
                schema: "erp",
                table: "operation_machine_overhead_settlement_voids",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "settlement_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operation_machine_overhead_settlements_work_center_machine_~",
                schema: "erp",
                table: "operation_machine_overhead_settlements",
                column: "work_center_machine_overhead_rate_id");

            migrationBuilder.CreateIndex(
                name: "ix_operation_machine_overhead_settlements_work_order",
                schema: "erp",
                table: "operation_machine_overhead_settlements",
                columns: new[] { "organization_id", "environment_id", "work_order_id" });

            migrationBuilder.CreateIndex(
                name: "ux_op_machine_overhead_settlements_identity",
                schema: "erp",
                table: "operation_machine_overhead_settlements",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "settlement_revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                COMMENT ON TABLE erp.work_order_cost_details IS 'ERP auditable labor or material cost detail.';
                COMMENT ON COLUMN erp.work_order_cost_details.cost_type IS 'Labor or material cost type.';
                COMMENT ON COLUMN erp.work_order_cost_details.quantity IS 'Labor hours or material quantity.';
                COMMENT ON COLUMN erp.work_order_cost_details.rate IS 'Hourly rate or moving-average unit cost.';
                """);

            migrationBuilder.DropTable(
                name: "operation_machine_overhead_settlement_states",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "operation_machine_overhead_settlement_voids",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "operation_machine_overhead_settlements",
                schema: "erp");

            migrationBuilder.DropColumn(
                name: "machine_overhead_currency_code",
                schema: "erp",
                table: "work_order_costs");

            migrationBuilder.DropColumn(
                name: "machine_overhead_basis",
                schema: "erp",
                table: "work_order_cost_details");

            migrationBuilder.DropColumn(
                name: "machine_overhead_lineage_id",
                schema: "erp",
                table: "work_order_cost_details");
        }
    }
}
