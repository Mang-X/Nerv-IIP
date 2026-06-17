using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddErpIssue411BusinessGapClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "location_code",
                schema: "erp",
                table: "purchase_receipt_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Inventory receipt location code.");

            migrationBuilder.AddColumn<string>(
                name: "lot_no",
                schema: "erp",
                table: "purchase_receipt_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional received lot number.");

            migrationBuilder.AddColumn<string>(
                name: "sku_code",
                schema: "erp",
                table: "purchase_receipt_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "MasterData SKU code copied from purchase order line for stock posting.");

            migrationBuilder.AddColumn<string>(
                name: "uom_code",
                schema: "erp",
                table: "purchase_receipt_lines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "MasterData UOM code copied from purchase order line for stock posting.");

            migrationBuilder.AddColumn<string>(
                name: "location_code",
                schema: "erp",
                table: "delivery_order_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Outbound source location code.");

            migrationBuilder.AddColumn<string>(
                name: "lot_no",
                schema: "erp",
                table: "delivery_order_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional outbound lot number.");

            migrationBuilder.AddColumn<string>(
                name: "sku_code",
                schema: "erp",
                table: "delivery_order_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "MasterData SKU code copied from sales order line for WMS outbound execution.");

            migrationBuilder.AddColumn<string>(
                name: "uom_code",
                schema: "erp",
                table: "delivery_order_lines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "MasterData UOM code copied from sales order line for WMS outbound execution.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                schema: "erp",
                table: "account_receivables",
                type: "date",
                nullable: false,
                defaultValueSql: "CURRENT_DATE",
                comment: "Collection due date.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "invoice_date",
                schema: "erp",
                table: "account_receivables",
                type: "date",
                nullable: false,
                defaultValueSql: "CURRENT_DATE",
                comment: "Customer invoice date.");

            migrationBuilder.AddColumn<string>(
                name: "payment_term_code",
                schema: "erp",
                table: "account_receivables",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "Payment term code snapshot.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                schema: "erp",
                table: "account_payables",
                type: "date",
                nullable: false,
                defaultValueSql: "CURRENT_DATE",
                comment: "Payment due date.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "invoice_date",
                schema: "erp",
                table: "account_payables",
                type: "date",
                nullable: false,
                defaultValueSql: "CURRENT_DATE",
                comment: "Supplier invoice date.");

            migrationBuilder.AddColumn<string>(
                name: "payment_term_code",
                schema: "erp",
                table: "account_payables",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "Payment term code snapshot.");

            migrationBuilder.CreateTable(
                name: "supplier_invoices",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Supplier invoice aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    invoice_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Supplier invoice number."),
                    purchase_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Matched purchase order number."),
                    purchase_receipt_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Matched purchase receipt number."),
                    supplier_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData supplier code."),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Supplier invoice date."),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Payment due date."),
                    currency_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, comment: "Invoice currency code."),
                    total_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Matched invoice total amount."),
                    match_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Three-way match status."),
                    matched_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC match time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_invoices", x => x.id);
                },
                comment: "ERP supplier invoice header matched against purchase order and receipt.");

            migrationBuilder.CreateTable(
                name: "supplier_invoice_lines",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Supplier invoice line id."),
                    purchase_order_line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Matched purchase order line number."),
                    purchase_receipt_line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Matched purchase receipt line number."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Matched SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Matched UOM code."),
                    invoice_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Supplier invoice quantity."),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Supplier invoice unit price."),
                    supplier_invoice_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning supplier invoice id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_invoice_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_supplier_invoice_lines_supplier_invoices_supplier_invoice_id",
                        column: x => x.supplier_invoice_id,
                        principalSchema: "erp",
                        principalTable: "supplier_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP supplier invoice lines used for three-way match.");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoice_lines_supplier_invoice_id",
                schema: "erp",
                table: "supplier_invoice_lines",
                column: "supplier_invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_invoices_organization_id_environment_id_invoice_no",
                schema: "erp",
                table: "supplier_invoices",
                columns: new[] { "organization_id", "environment_id", "invoice_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_invoice_lines",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "supplier_invoices",
                schema: "erp");

            migrationBuilder.DropColumn(
                name: "location_code",
                schema: "erp",
                table: "purchase_receipt_lines");

            migrationBuilder.DropColumn(
                name: "lot_no",
                schema: "erp",
                table: "purchase_receipt_lines");

            migrationBuilder.DropColumn(
                name: "sku_code",
                schema: "erp",
                table: "purchase_receipt_lines");

            migrationBuilder.DropColumn(
                name: "uom_code",
                schema: "erp",
                table: "purchase_receipt_lines");

            migrationBuilder.DropColumn(
                name: "location_code",
                schema: "erp",
                table: "delivery_order_lines");

            migrationBuilder.DropColumn(
                name: "lot_no",
                schema: "erp",
                table: "delivery_order_lines");

            migrationBuilder.DropColumn(
                name: "sku_code",
                schema: "erp",
                table: "delivery_order_lines");

            migrationBuilder.DropColumn(
                name: "uom_code",
                schema: "erp",
                table: "delivery_order_lines");

            migrationBuilder.DropColumn(
                name: "due_date",
                schema: "erp",
                table: "account_receivables");

            migrationBuilder.DropColumn(
                name: "invoice_date",
                schema: "erp",
                table: "account_receivables");

            migrationBuilder.DropColumn(
                name: "payment_term_code",
                schema: "erp",
                table: "account_receivables");

            migrationBuilder.DropColumn(
                name: "due_date",
                schema: "erp",
                table: "account_payables");

            migrationBuilder.DropColumn(
                name: "invoice_date",
                schema: "erp",
                table: "account_payables");

            migrationBuilder.DropColumn(
                name: "payment_term_code",
                schema: "erp",
                table: "account_payables");
        }
    }
}
