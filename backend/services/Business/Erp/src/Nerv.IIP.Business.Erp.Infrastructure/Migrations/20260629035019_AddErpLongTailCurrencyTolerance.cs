using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddErpLongTailCurrencyTolerance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currency_code",
                schema: "erp",
                table: "purchase_orders",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "CNY",
                comment: "Purchase order currency code.");

            migrationBuilder.AddColumn<bool>(
                name: "final_delivery",
                schema: "erp",
                table: "purchase_order_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether final delivery was declared and the line is closed despite remaining quantity.");

            migrationBuilder.AddColumn<decimal>(
                name: "over_receipt_tolerance_percent",
                schema: "erp",
                table: "purchase_order_lines",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                comment: "Allowed over receipt tolerance percent for the line.");

            migrationBuilder.AddColumn<decimal>(
                name: "under_receipt_tolerance_percent",
                schema: "erp",
                table: "purchase_order_lines",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                comment: "Allowed under receipt tolerance percent for final delivery close.");

            migrationBuilder.AddColumn<string>(
                name: "currency_code",
                schema: "erp",
                table: "journal_voucher_lines",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "CNY",
                comment: "Voucher line currency code.");

            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                schema: "erp",
                table: "journal_voucher_lines",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m,
                comment: "Voucher line exchange rate to local currency.");

            migrationBuilder.AddColumn<decimal>(
                name: "local_credit_amount",
                schema: "erp",
                table: "journal_voucher_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Credit amount in local currency.");

            migrationBuilder.AddColumn<decimal>(
                name: "local_debit_amount",
                schema: "erp",
                table: "journal_voucher_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Debit amount in local currency.");

            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                schema: "erp",
                table: "account_receivables",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m,
                comment: "Document exchange rate to local currency.");

            migrationBuilder.AddColumn<decimal>(
                name: "local_amount",
                schema: "erp",
                table: "account_receivables",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Local currency amount at document exchange rate.");

            migrationBuilder.AddColumn<decimal>(
                name: "local_collected_amount",
                schema: "erp",
                table: "account_receivables",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Local currency collected amount at document exchange rate.");

            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                schema: "erp",
                table: "account_payables",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m,
                comment: "Document exchange rate to local currency.");

            migrationBuilder.AddColumn<decimal>(
                name: "local_amount",
                schema: "erp",
                table: "account_payables",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Local currency amount at document exchange rate.");

            migrationBuilder.AddColumn<decimal>(
                name: "local_paid_amount",
                schema: "erp",
                table: "account_payables",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Local currency paid amount at document exchange rate.");

            migrationBuilder.Sql("""
                UPDATE erp.journal_voucher_lines
                SET local_debit_amount = debit_amount,
                    local_credit_amount = credit_amount
                """);

            migrationBuilder.Sql("""
                UPDATE erp.account_receivables
                SET local_amount = amount,
                    local_collected_amount = collected_amount
                """);

            migrationBuilder.Sql("""
                UPDATE erp.account_payables
                SET local_amount = amount,
                    local_paid_amount = paid_amount
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "currency_code",
                schema: "erp",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "final_delivery",
                schema: "erp",
                table: "purchase_order_lines");

            migrationBuilder.DropColumn(
                name: "over_receipt_tolerance_percent",
                schema: "erp",
                table: "purchase_order_lines");

            migrationBuilder.DropColumn(
                name: "under_receipt_tolerance_percent",
                schema: "erp",
                table: "purchase_order_lines");

            migrationBuilder.DropColumn(
                name: "currency_code",
                schema: "erp",
                table: "journal_voucher_lines");

            migrationBuilder.DropColumn(
                name: "exchange_rate",
                schema: "erp",
                table: "journal_voucher_lines");

            migrationBuilder.DropColumn(
                name: "local_credit_amount",
                schema: "erp",
                table: "journal_voucher_lines");

            migrationBuilder.DropColumn(
                name: "local_debit_amount",
                schema: "erp",
                table: "journal_voucher_lines");

            migrationBuilder.DropColumn(
                name: "exchange_rate",
                schema: "erp",
                table: "account_receivables");

            migrationBuilder.DropColumn(
                name: "local_amount",
                schema: "erp",
                table: "account_receivables");

            migrationBuilder.DropColumn(
                name: "local_collected_amount",
                schema: "erp",
                table: "account_receivables");

            migrationBuilder.DropColumn(
                name: "exchange_rate",
                schema: "erp",
                table: "account_payables");

            migrationBuilder.DropColumn(
                name: "local_amount",
                schema: "erp",
                table: "account_payables");

            migrationBuilder.DropColumn(
                name: "local_paid_amount",
                schema: "erp",
                table: "account_payables");
        }
    }
}
