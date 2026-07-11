using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddErpReturnSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "credit_note_amount",
                schema: "erp",
                table: "account_receivables",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Applied customer credit-note amount.");

            migrationBuilder.AddColumn<decimal>(
                name: "local_credit_note_amount",
                schema: "erp",
                table: "account_receivables",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Applied customer credit-note local amount.");

            migrationBuilder.AddColumn<decimal>(
                name: "debit_note_amount",
                schema: "erp",
                table: "account_payables",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Applied supplier debit-note amount.");

            migrationBuilder.AddColumn<decimal>(
                name: "local_debit_note_amount",
                schema: "erp",
                table: "account_payables",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Applied supplier debit-note local amount.");

            migrationBuilder.CreateTable(
                name: "credit_notes",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Credit note aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    credit_note_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Customer credit note number."),
                    rma_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source RMA number."),
                    account_receivable_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "AR document settled by this credit."),
                    customer_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Customer code."),
                    currency_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, comment: "Credit note currency."),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false, comment: "Credit note exchange rate."),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Credit amount."),
                    local_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Credit local amount."),
                    issued_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC issue time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_notes", x => x.id);
                },
                comment: "ERP customer credit note issued after RMA Quality disposition.");

            migrationBuilder.CreateTable(
                name: "debit_notes",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Debit note aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    debit_note_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Supplier debit note number."),
                    purchase_return_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source purchase return number."),
                    payable_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "AP document reduced by this note."),
                    supplier_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Supplier code."),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Debit note amount."),
                    currency_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, comment: "Debit note currency."),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false, comment: "Debit note exchange rate."),
                    local_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Debit note local amount."),
                    issued_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC issue time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_debit_notes", x => x.id);
                },
                comment: "ERP supplier debit note applied to an open AP after purchase return.");

            migrationBuilder.CreateTable(
                name: "purchase_returns",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Purchase return aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    purchase_return_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "ERP purchase return document number."),
                    purchase_receipt_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Immutable ERP receipt being compensated."),
                    wms_outbound_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Completed WMS supplier-return outbound reference."),
                    supplier_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Supplier copied from the source receipt."),
                    currency_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, comment: "Return currency copied from the source receipt."),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false, comment: "Return exchange rate to local currency."),
                    recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time ERP recorded the completed physical return.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_returns", x => x.id);
                },
                comment: "ERP immutable supplier purchase return recorded from completed WMS outbound.");

            migrationBuilder.CreateTable(
                name: "sales_return_authorizations",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "RMA aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    rma_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Customer return authorization number."),
                    sales_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source ERP sales order number."),
                    account_receivable_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Open AR to settle by credit note."),
                    customer_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source customer code."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Return receiving site."),
                    currency_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, comment: "RMA credit currency."),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false, comment: "RMA exchange rate to local currency."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "RMA lifecycle status."),
                    wms_inbound_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Actual WMS customer-return inbound order reference."),
                    quality_disposition = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, comment: "Quality result that permits or denies the credit."),
                    credit_note_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Issued ERP credit note reference."),
                    authorized_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC authorization time."),
                    warehouse_received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC WMS inbound completion projection time."),
                    quality_disposition_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC Quality disposition projection time."),
                    credit_issued_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC credit note issuance time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_return_authorizations", x => x.id);
                },
                comment: "ERP customer RMA authorization and Quality-gated credit lifecycle.");

            migrationBuilder.CreateTable(
                name: "purchase_return_lines",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Purchase return line id."),
                    purchase_order_line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source purchase receipt purchase-order line reference."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Source UOM code."),
                    returned_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Physically returned WMS quantity."),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "ERP source purchase order unit price."),
                    gr_ir_reversal_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Uninvoiced returned quantity reversing GR/IR."),
                    debit_note_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Invoice-matched returned quantity settled by debit note."),
                    purchase_return_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning purchase return id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_return_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_return_lines_purchase_returns_purchase_return_id",
                        column: x => x.purchase_return_id,
                        principalSchema: "erp",
                        principalTable: "purchase_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP purchase return line with GR/IR and debit-note quantity split.");

            migrationBuilder.CreateTable(
                name: "sales_return_authorization_lines",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "RMA line id."),
                    sales_order_line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source sales order line number."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Source UOM code."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Authorized return quantity."),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Source sales unit price for credit."),
                    location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "WMS return receiving location."),
                    lot_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional expected return lot."),
                    sales_return_authorization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning RMA id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_return_authorization_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_sales_return_authorization_lines_sales_return_authorization~",
                        column: x => x.sales_return_authorization_id,
                        principalSchema: "erp",
                        principalTable: "sales_return_authorizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP RMA source sales line and requested return quantity.");

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_organization_id_environment_id_credit_note_no",
                schema: "erp",
                table: "credit_notes",
                columns: new[] { "organization_id", "environment_id", "credit_note_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_organization_id_environment_id_rma_no",
                schema: "erp",
                table: "credit_notes",
                columns: new[] { "organization_id", "environment_id", "rma_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_debit_notes_organization_id_environment_id_debit_note_no",
                schema: "erp",
                table: "debit_notes",
                columns: new[] { "organization_id", "environment_id", "debit_note_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_return_lines_purchase_return_id",
                schema: "erp",
                table: "purchase_return_lines",
                column: "purchase_return_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_organization_id_environment_id_purchase_re~",
                schema: "erp",
                table: "purchase_returns",
                columns: new[] { "organization_id", "environment_id", "purchase_return_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_returns_organization_id_environment_id_wms_outboun~",
                schema: "erp",
                table: "purchase_returns",
                columns: new[] { "organization_id", "environment_id", "wms_outbound_order_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_authorization_lines_sales_return_authorization~",
                schema: "erp",
                table: "sales_return_authorization_lines",
                column: "sales_return_authorization_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_authorizations_organization_id_environment_id_~",
                schema: "erp",
                table: "sales_return_authorizations",
                columns: new[] { "organization_id", "environment_id", "rma_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_return_authorizations_organization_id_environment_id~1",
                schema: "erp",
                table: "sales_return_authorizations",
                columns: new[] { "organization_id", "environment_id", "wms_inbound_order_no" },
                unique: true,
                filter: "wms_inbound_order_no IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_notes",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "debit_notes",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "purchase_return_lines",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "sales_return_authorization_lines",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "purchase_returns",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "sales_return_authorizations",
                schema: "erp");

            migrationBuilder.DropColumn(
                name: "credit_note_amount",
                schema: "erp",
                table: "account_receivables");

            migrationBuilder.DropColumn(
                name: "local_credit_note_amount",
                schema: "erp",
                table: "account_receivables");

            migrationBuilder.DropColumn(
                name: "debit_note_amount",
                schema: "erp",
                table: "account_payables");

            migrationBuilder.DropColumn(
                name: "local_debit_note_amount",
                schema: "erp",
                table: "account_payables");
        }
    }
}
