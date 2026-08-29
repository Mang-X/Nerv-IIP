using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkCenterMachineOverheadReconciliations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_wc_machine_overhead_rates_hard_scope",
                schema: "erp",
                table: "work_center_machine_overhead_rates",
                columns: new[] { "id", "organization_id", "environment_id", "work_center_id", "accounting_period_code", "revision" });

            migrationBuilder.CreateTable(
                name: "work_center_machine_overhead_reconciliations",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Machine-overhead reconciliation revision id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization boundary."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment boundary."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Work center whose actual pool is reconciled."),
                    accounting_period_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Accounting period owning this reconciliation."),
                    work_center_machine_overhead_rate_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Predetermined monthly rate revision used by this reconciliation."),
                    rate_revision = table.Column<int>(type: "integer", nullable: false, comment: "Frozen predetermined rate revision number."),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, comment: "Currency shared by actual pool, rate, and applied settlements."),
                    actual_fixed_overhead_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Actual fixed manufacturing-overhead pool."),
                    actual_variable_overhead_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Actual variable manufacturing-overhead pool."),
                    actual_total_overhead_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Actual fixed plus variable manufacturing-overhead pool."),
                    applied_machine_ticks = table.Column<long>(type: "bigint", nullable: false, comment: "Lossless active billable machine ticks allocated to products; abnormal downtime is excluded."),
                    applied_machine_hours = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: false, comment: "Display hours derived from active applied ticks."),
                    applied_fixed_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Fixed overhead already allocated by active settlements."),
                    applied_variable_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Variable overhead already allocated by active settlements."),
                    applied_total_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Total overhead already allocated by active settlements."),
                    applied_rounding_difference_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Difference caused by independently rounded total versus fixed and variable applied amounts."),
                    under_over_applied_fixed_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Signed actual-minus-applied fixed overhead variance."),
                    under_over_applied_variable_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Signed actual-minus-applied variable overhead variance."),
                    under_over_applied_total_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Signed actual-minus-applied total overhead variance."),
                    unallocated_fixed_overhead_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Positive fixed overhead left unallocated at low utilization."),
                    over_applied_fixed_overhead_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Positive reverse fixed variance when applied overhead exceeds the actual pool."),
                    abnormal_downtime_ticks = table.Column<long>(type: "bigint", nullable: false, comment: "Abnormal downtime ticks excluded from product allocation."),
                    abnormal_downtime_hours = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: false, comment: "Display hours derived from abnormal downtime ticks."),
                    abnormal_downtime_disposition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "None, Pending, or PeriodExpense close disposition."),
                    revision = table.Column<int>(type: "integer", nullable: false, comment: "Monotonic append-only reconciliation revision within work center and period."),
                    recorded_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Canonical authenticated actor recording the actual pool."),
                    source_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false, comment: "Auditable source ledger, import, or worksheet reference."),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Business reason for this immutable revision."),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC instant when this revision was recorded.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_center_machine_overhead_reconciliations", x => x.id);
                    table.CheckConstraint("ck_wc_machine_overhead_reconciliations_amounts", "actual_fixed_overhead_amount >= 0 AND actual_variable_overhead_amount >= 0 AND actual_total_overhead_amount = actual_fixed_overhead_amount + actual_variable_overhead_amount AND applied_machine_ticks >= 0 AND applied_machine_hours = applied_machine_ticks / 36000000000.0 AND applied_fixed_amount >= 0 AND applied_variable_amount >= 0 AND applied_total_amount >= 0 AND applied_rounding_difference_amount = applied_total_amount - applied_fixed_amount - applied_variable_amount AND under_over_applied_fixed_amount = actual_fixed_overhead_amount - applied_fixed_amount AND under_over_applied_variable_amount = actual_variable_overhead_amount - applied_variable_amount AND under_over_applied_total_amount = actual_total_overhead_amount - applied_total_amount AND unallocated_fixed_overhead_amount = greatest(under_over_applied_fixed_amount, 0) AND over_applied_fixed_overhead_amount = greatest(-under_over_applied_fixed_amount, 0)");
                    table.CheckConstraint("ck_wc_machine_overhead_reconciliations_audit", "revision > 0 AND rate_revision > 0 AND currency_code ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_wc_machine_overhead_reconciliations_downtime", "abnormal_downtime_hours = abnormal_downtime_ticks / 36000000000.0 AND ((abnormal_downtime_ticks = 0 AND abnormal_downtime_disposition = 'None') OR (abnormal_downtime_ticks > 0 AND abnormal_downtime_disposition IN ('Pending', 'PeriodExpense')))");
                    table.ForeignKey(
                        name: "FK_work_center_machine_overhead_reconciliations_work_center_ma~",
                        columns: x => new { x.work_center_machine_overhead_rate_id, x.organization_id, x.environment_id, x.work_center_id, x.accounting_period_code, x.rate_revision },
                        principalSchema: "erp",
                        principalTable: "work_center_machine_overhead_rates",
                        principalColumns: new[] { "id", "organization_id", "environment_id", "work_center_id", "accounting_period_code", "revision" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Append-only monthly work-center machine-overhead pool reconciliation and close readiness fact.");

            migrationBuilder.CreateIndex(
                name: "ix_wc_machine_overhead_reconciliations_period",
                schema: "erp",
                table: "work_center_machine_overhead_reconciliations",
                columns: new[] { "organization_id", "environment_id", "accounting_period_code", "work_center_id" });

            migrationBuilder.CreateIndex(
                name: "IX_work_center_machine_overhead_reconciliations_work_center_ma~",
                schema: "erp",
                table: "work_center_machine_overhead_reconciliations",
                columns: new[] { "work_center_machine_overhead_rate_id", "organization_id", "environment_id", "work_center_id", "accounting_period_code", "rate_revision" });

            migrationBuilder.CreateIndex(
                name: "ux_wc_machine_overhead_reconciliations_scope_revision",
                schema: "erp",
                table: "work_center_machine_overhead_reconciliations",
                columns: new[] { "organization_id", "environment_id", "work_center_id", "accounting_period_code", "revision" },
                unique: true,
                descending: new[] { false, false, false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_center_machine_overhead_reconciliations",
                schema: "erp");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_wc_machine_overhead_rates_hard_scope",
                schema: "erp",
                table: "work_center_machine_overhead_rates");
        }
    }
}
