using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddErpCostAccountingPhaseOne : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "environment_id",
                schema: "erp",
                table: "journal_voucher_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Environment boundary copied from the voucher for GL account linkage.");

            migrationBuilder.AddColumn<string>(
                name: "organization_id",
                schema: "erp",
                table: "journal_voucher_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Organization boundary copied from the voucher for GL account linkage.");

            migrationBuilder.CreateTable(
                name: "gl_accounts",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "GL account aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization boundary."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment boundary."),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Tenant-unique GL account code."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "GL account display name."),
                    account_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Asset, liability, equity, revenue, or expense classification."),
                    parent_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional parent GL account code in the same tenant.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gl_accounts", x => x.id);
                    table.UniqueConstraint("AK_gl_accounts_organization_id_environment_id_code", x => new { x.organization_id, x.environment_id, x.code });
                },
                comment: "ERP general-ledger account hierarchy.");

            migrationBuilder.Sql(
                """
                UPDATE erp.journal_voucher_lines AS line
                SET organization_id = voucher.organization_id,
                    environment_id = voucher.environment_id
                FROM erp.journal_vouchers AS voucher
                WHERE voucher.id = line.journal_voucher_id;

                INSERT INTO erp.gl_accounts (id, organization_id, environment_id, code, name, account_type, parent_code)
                SELECT uuidv7(), line.organization_id, line.environment_id, line.account_code,
                       'Migrated account ' || line.account_code,
                       CASE LEFT(line.account_code, 1)
                           WHEN '1' THEN 'Asset' WHEN '2' THEN 'Liability'
                           WHEN '3' THEN 'Equity' WHEN '4' THEN 'Revenue'
                           ELSE 'Expense'
                       END,
                       NULL
                FROM erp.journal_voucher_lines AS line
                GROUP BY line.organization_id, line.environment_id, line.account_code;
                """);

            migrationBuilder.CreateTable(
                name: "pending_material_costs",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Pending material cost id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization boundary."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment boundary."),
                    movement_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Inventory movement public id."),
                    report_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES report number used for later correlation."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Consumed material SKU."),
                    signed_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Positive actual consumption or negative reversal quantity."),
                    unit_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Inventory moving-average unit cost."),
                    posted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Inventory posting timestamp.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_material_costs", x => x.id);
                },
                comment: "Order-independent Inventory material cost awaiting its MES report projection.");

            migrationBuilder.CreateTable(
                name: "work_center_cost_rates",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Work-center cost-rate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization boundary."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment boundary."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work-center public identifier."),
                    hourly_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Actual labor rate per hour in local currency.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_center_cost_rates", x => x.id);
                },
                comment: "ERP phase-one actual labor hourly rates by work center.");

            migrationBuilder.CreateTable(
                name: "work_order_costs",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Work-order cost aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization boundary."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment boundary."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work-order public identifier."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Finished-good SKU code."),
                    completed_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "MES good quantity at completion."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "MES completion timestamp."),
                    expected_report_count = table.Column<int>(type: "integer", nullable: false, comment: "MES completion count of cost-bearing reports."),
                    received_report_count = table.Column<int>(type: "integer", nullable: false, comment: "Cost-bearing reports received by ERP."),
                    expected_material_movement_count = table.Column<int>(type: "integer", nullable: false, comment: "MES completion count of expected material postings."),
                    received_material_movement_count = table.Column<int>(type: "integer", nullable: false, comment: "Actual Inventory material postings received by ERP."),
                    capitalization_published = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the cost-ready capitalization event has been published."),
                    capitalized_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Finished-goods quantity posted for this work order."),
                    wip_cleared_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Cumulative WIP amount cleared by capitalization vouchers."),
                    capitalized_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Finished-goods inventory value posted for this work order.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_costs", x => x.id);
                },
                comment: "ERP actual work-order cost accumulation and capitalization fact.");

            migrationBuilder.CreateTable(
                name: "work_order_cost_details",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Cost detail id."),
                    cost_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Labor or material cost type."),
                    source_document_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Public source event document id."),
                    dimension_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Work center or material SKU dimension."),
                    report_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "MES report number for material-to-work-order correlation."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Labor hours or material quantity."),
                    rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Hourly rate or moving-average unit cost."),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Signed actual cost amount."),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Source fact occurrence timestamp."),
                    work_order_cost_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning work-order cost id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_cost_details", x => x.id);
                    table.ForeignKey(
                        name: "FK_work_order_cost_details_work_order_costs_work_order_cost_id",
                        column: x => x.work_order_cost_id,
                        principalSchema: "erp",
                        principalTable: "work_order_costs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP auditable labor or material cost detail.");

            migrationBuilder.CreateIndex(
                name: "IX_journal_voucher_lines_organization_id_environment_id_accoun~",
                schema: "erp",
                table: "journal_voucher_lines",
                columns: new[] { "organization_id", "environment_id", "account_code" });

            migrationBuilder.CreateIndex(
                name: "IX_gl_accounts_organization_id_environment_id_code",
                schema: "erp",
                table: "gl_accounts",
                columns: new[] { "organization_id", "environment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pending_material_costs_organization_id_environment_id_movem~",
                schema: "erp",
                table: "pending_material_costs",
                columns: new[] { "organization_id", "environment_id", "movement_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pending_material_costs_organization_id_environment_id_repor~",
                schema: "erp",
                table: "pending_material_costs",
                columns: new[] { "organization_id", "environment_id", "report_no" });

            migrationBuilder.CreateIndex(
                name: "IX_work_center_cost_rates_organization_id_environment_id_work_~",
                schema: "erp",
                table: "work_center_cost_rates",
                columns: new[] { "organization_id", "environment_id", "work_center_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_order_cost_details_work_order_cost_id_source_document_~",
                schema: "erp",
                table: "work_order_cost_details",
                columns: new[] { "work_order_cost_id", "source_document_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_order_costs_organization_id_environment_id_work_order_~",
                schema: "erp",
                table: "work_order_costs",
                columns: new[] { "organization_id", "environment_id", "work_order_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_journal_voucher_lines_gl_accounts_organization_id_environme~",
                schema: "erp",
                table: "journal_voucher_lines",
                columns: new[] { "organization_id", "environment_id", "account_code" },
                principalSchema: "erp",
                principalTable: "gl_accounts",
                principalColumns: new[] { "organization_id", "environment_id", "code" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_journal_voucher_lines_gl_accounts_organization_id_environme~",
                schema: "erp",
                table: "journal_voucher_lines");

            migrationBuilder.DropTable(
                name: "gl_accounts",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "pending_material_costs",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "work_center_cost_rates",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "work_order_cost_details",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "work_order_costs",
                schema: "erp");

            migrationBuilder.DropIndex(
                name: "IX_journal_voucher_lines_organization_id_environment_id_accoun~",
                schema: "erp",
                table: "journal_voucher_lines");

            migrationBuilder.DropColumn(
                name: "environment_id",
                schema: "erp",
                table: "journal_voucher_lines");

            migrationBuilder.DropColumn(
                name: "organization_id",
                schema: "erp",
                table: "journal_voucher_lines");
        }
    }
}
