using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddErpPeriodPaymentReceiptClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "erp",
                table: "integration_event_dead_letters",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Dead-letter status: Pending, Replayed, Failed, or Ignored.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Dead-letter status: Pending or Replayed.");

            migrationBuilder.CreateTable(
                name: "accounting_periods",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Accounting period aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    period_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Accounting period code such as fiscal month."),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Inclusive period start date."),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Inclusive period end date."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Accounting period status."),
                    opened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when period was opened."),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC time when period was closed."),
                    closed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "User or service that closed the period."),
                    close_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Auditable close reason."),
                    reopened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC time when period was reopened for exception handling."),
                    reopened_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "User or service that reopened the period."),
                    reopen_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Auditable reopen or exception reason.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_periods", x => x.id);
                },
                comment: "ERP accounting period open and close control.");

            migrationBuilder.CreateTable(
                name: "cash_receipts",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Cash receipt aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    cash_receipt_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Cash receipt document number."),
                    customer_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData customer code."),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Receipt amount."),
                    currency_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, comment: "Receipt currency code."),
                    receipt_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Cash receipt date."),
                    cash_account_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Cash or bank account code used by receipt."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Cash receipt status."),
                    registered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC registration time."),
                    matched_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC matching time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_receipts", x => x.id);
                },
                comment: "ERP cash receipt document for AR matching.");

            migrationBuilder.CreateTable(
                name: "payment_executions",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Payment execution aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    payment_execution_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Payment execution document number."),
                    supplier_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData supplier code."),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Payment amount."),
                    currency_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, comment: "Payment currency code."),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Payment execution date."),
                    cash_account_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Cash or bank account code used by payment."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Payment execution status."),
                    approved_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Approver user or service."),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC approval time."),
                    executed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Executor user or service."),
                    executed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC execution time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_executions", x => x.id);
                },
                comment: "ERP payment execution document for AP settlement.");

            migrationBuilder.CreateTable(
                name: "cash_receipt_allocations",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Cash receipt allocation id."),
                    receivable_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Allocated AR document number."),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Allocated receipt amount."),
                    cash_receipt_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning cash receipt id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_receipt_allocations", x => x.id);
                    table.ForeignKey(
                        name: "FK_cash_receipt_allocations_cash_receipts_cash_receipt_id",
                        column: x => x.cash_receipt_id,
                        principalSchema: "erp",
                        principalTable: "cash_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP cash receipt allocation to AR documents.");

            migrationBuilder.CreateTable(
                name: "payment_execution_allocations",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Payment execution allocation id."),
                    payable_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Allocated AP document number."),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Allocated payment amount."),
                    payment_execution_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning payment execution id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_execution_allocations", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_execution_allocations_payment_executions_payment_ex~",
                        column: x => x.payment_execution_id,
                        principalSchema: "erp",
                        principalTable: "payment_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP payment execution allocation to AP documents.");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_periods_organization_id_environment_id_period_co~",
                schema: "erp",
                table: "accounting_periods",
                columns: new[] { "organization_id", "environment_id", "period_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_periods_organization_id_environment_id_start_dat~",
                schema: "erp",
                table: "accounting_periods",
                columns: new[] { "organization_id", "environment_id", "start_date", "end_date" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_receipt_allocations_cash_receipt_id",
                schema: "erp",
                table: "cash_receipt_allocations",
                column: "cash_receipt_id");

            migrationBuilder.CreateIndex(
                name: "IX_cash_receipts_organization_id_environment_id_cash_receipt_no",
                schema: "erp",
                table: "cash_receipts",
                columns: new[] { "organization_id", "environment_id", "cash_receipt_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_execution_allocations_payment_execution_id",
                schema: "erp",
                table: "payment_execution_allocations",
                column: "payment_execution_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_executions_organization_id_environment_id_payment_e~",
                schema: "erp",
                table: "payment_executions",
                columns: new[] { "organization_id", "environment_id", "payment_execution_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_periods",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "cash_receipt_allocations",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "payment_execution_allocations",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "cash_receipts",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "payment_executions",
                schema: "erp");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "erp",
                table: "integration_event_dead_letters",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Dead-letter status: Pending or Replayed.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Dead-letter status: Pending, Replayed, Failed, or Ignored.");
        }
    }
}
