using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkCenterMachineOverheadRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_center_machine_overhead_rates",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Work-center machine-overhead rate revision id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization boundary."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment boundary."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Work-center public identifier that owns the monthly machine-overhead pool."),
                    accounting_period_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "ERP accounting period code for this monthly rate revision."),
                    applicability = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Explicit Applicable or NotApplicable status for machine-overhead allocation."),
                    fixed_overhead_budget = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Monthly fixed manufacturing-overhead budget for the work center."),
                    variable_overhead_budget = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Monthly variable manufacturing-overhead budget for the work center."),
                    normal_capacity_machine_hours = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Normal-capacity machine hours excluding planned maintenance; never actual low-load hours."),
                    fixed_hourly_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "System-derived fixed overhead budget divided by normal-capacity machine hours."),
                    variable_hourly_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "System-derived variable overhead budget divided by normal-capacity machine hours."),
                    total_hourly_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "System-derived sum of fixed and variable machine-overhead hourly rates."),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, comment: "Normalized three-letter uppercase currency code fixed within the work-center scope."),
                    revision = table.Column<int>(type: "integer", nullable: false, comment: "Monotonically increasing append-only revision within scope, work center, and accounting period."),
                    changed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Canonical authenticated actor that configured this immutable revision."),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Auditable business reason for this immutable revision."),
                    changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC audit instant at which this revision was configured.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_center_machine_overhead_rates", x => x.id);
                    table.CheckConstraint("ck_wc_machine_overhead_rates_cost_basis", "(applicability = 'Applicable'\n AND fixed_overhead_budget >= 0\n AND variable_overhead_budget >= 0\n AND fixed_overhead_budget + variable_overhead_budget > 0\n AND normal_capacity_machine_hours > 0\n AND fixed_hourly_rate = round(fixed_overhead_budget / normal_capacity_machine_hours, 6)\n AND variable_hourly_rate = round(variable_overhead_budget / normal_capacity_machine_hours, 6)\n AND total_hourly_rate = fixed_hourly_rate + variable_hourly_rate)\nOR\n(applicability = 'NotApplicable'\n AND fixed_overhead_budget = 0\n AND variable_overhead_budget = 0\n AND normal_capacity_machine_hours = 0\n AND fixed_hourly_rate = 0\n AND variable_hourly_rate = 0\n AND total_hourly_rate = 0)");
                    table.CheckConstraint("ck_wc_machine_overhead_rates_currency_revision", "currency_code ~ '^[A-Z]{3}$' AND revision > 0");
                },
                comment: "ERP append-only monthly predetermined machine-overhead rate revisions by work center.");

            migrationBuilder.CreateIndex(
                name: "ux_wc_machine_overhead_rates_scope_period_revision",
                schema: "erp",
                table: "work_center_machine_overhead_rates",
                columns: new[] { "organization_id", "environment_id", "work_center_id", "accounting_period_code", "revision" },
                unique: true,
                descending: new[] { false, false, false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_center_machine_overhead_rates",
                schema: "erp");
        }
    }
}
