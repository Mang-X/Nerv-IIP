using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialErpProcurementSalesFinanceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "erp");

            migrationBuilder.CreateTable(
                name: "account_payables",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Account payable aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    payable_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "AP document number."),
                    source_document_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source document number."),
                    supplier_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData supplier code."),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Document amount."),
                    paid_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Paid amount."),
                    currency_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, comment: "Currency code."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC creation time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_payables", x => x.id);
                },
                comment: "ERP account payable candidate fact.");

            migrationBuilder.CreateTable(
                name: "account_receivables",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Account receivable aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    receivable_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "AR document number."),
                    source_document_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source document number."),
                    customer_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData customer code."),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Document amount."),
                    collected_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Collected amount."),
                    currency_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, comment: "Currency code."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC creation time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_receivables", x => x.id);
                },
                comment: "ERP account receivable candidate fact.");

            migrationBuilder.CreateTable(
                name: "cap_locks",
                schema: "erp",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Instance = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastLockTime = table.Column<DateTime>(type: "TIMESTAMP", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_locks", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "cap_published_messages",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: true),
                    Added = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    StatusName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_published_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cap_received_messages",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Group = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: true),
                    Added = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    StatusName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_received_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cost_candidates",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Cost candidate aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    candidate_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Cost candidate number."),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Public source fact type."),
                    source_document_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Public source document number."),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Candidate amount."),
                    currency_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, comment: "Currency code."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC creation time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cost_candidates", x => x.id);
                },
                comment: "ERP cost candidate fact from public production, inventory or WMS facts.");

            migrationBuilder.CreateTable(
                name: "delivery_orders",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Delivery order aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    delivery_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Delivery order request number."),
                    sales_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source sales order number."),
                    customer_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData customer code."),
                    released_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC release time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_orders", x => x.id);
                },
                comment: "ERP delivery order request header for WMS outbound execution.");

            migrationBuilder.CreateTable(
                name: "journal_vouchers",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Journal voucher aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    voucher_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Voucher number."),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Voucher posting date."),
                    posted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC posting time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_vouchers", x => x.id);
                },
                comment: "ERP balanced journal voucher posting fact.");

            migrationBuilder.CreateTable(
                name: "opportunities",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Opportunity aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    opportunity_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Opportunity number."),
                    customer_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData customer code."),
                    topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Opportunity topic."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Opportunity lifecycle status."),
                    opened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC opportunity open time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opportunities", x => x.id);
                },
                comment: "ERP sales opportunity header.");

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Purchase order aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    purchase_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Purchase order number."),
                    supplier_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData supplier code."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Purchase order status."),
                    total_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Purchase order total amount."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC creation time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_orders", x => x.id);
                },
                comment: "ERP purchase order header.");

            migrationBuilder.CreateTable(
                name: "purchase_receipts",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Purchase receipt aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    purchase_receipt_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Purchase receipt number."),
                    purchase_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Referenced purchase order number."),
                    supplier_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData supplier code."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code."),
                    quality_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Receipt quality state summary."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Purchase receipt status."),
                    recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC recording time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_receipts", x => x.id);
                },
                comment: "ERP purchase receipt header.");

            migrationBuilder.CreateTable(
                name: "purchase_requisitions",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Purchase requisition aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    requisition_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Purchase requisition number."),
                    suggestion_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "DemandPlanning suggestion reference id."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData unit of measure code."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Required procurement quantity."),
                    required_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Required date from planning."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Purchase requisition status."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC creation time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_requisitions", x => x.id);
                },
                comment: "ERP procurement purchase requisition from planning or manual demand.");

            migrationBuilder.CreateTable(
                name: "quotations",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Quotation aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    quotation_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Quotation number."),
                    customer_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData customer code."),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: false, comment: "Quotation expiration date."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Quotation approval status."),
                    total_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Quotation total amount."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC quotation creation time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotations", x => x.id);
                },
                comment: "ERP customer quotation header.");

            migrationBuilder.CreateTable(
                name: "request_for_quotations",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Request for quotation aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    rfq_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "RFQ number."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "RFQ status."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC creation time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_for_quotations", x => x.id);
                },
                comment: "ERP request for quotation header.");

            migrationBuilder.CreateTable(
                name: "sales_orders",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Sales order aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    sales_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Sales order number."),
                    quotation_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source quotation number."),
                    customer_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData customer code."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Sales order lifecycle status."),
                    total_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Sales order total amount."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC creation time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_orders", x => x.id);
                },
                comment: "ERP sales order header.");

            migrationBuilder.CreateTable(
                name: "supplier_quotations",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Supplier quotation aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    quotation_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Supplier quotation number."),
                    rfq_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Referenced RFQ number."),
                    supplier_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData supplier code."),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC quotation receipt time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_quotations", x => x.id);
                },
                comment: "ERP supplier quotation header.");

            migrationBuilder.CreateTable(
                name: "delivery_order_lines",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Delivery order line id."),
                    sales_order_line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Referenced sales order line number."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Requested delivery quantity."),
                    delivery_order_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning delivery order id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_delivery_order_lines_delivery_orders_delivery_order_id",
                        column: x => x.delivery_order_id,
                        principalSchema: "erp",
                        principalTable: "delivery_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP delivery order request lines.");

            migrationBuilder.CreateTable(
                name: "journal_voucher_lines",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Journal voucher line id."),
                    account_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Accounting subject code."),
                    debit_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Debit amount."),
                    credit_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Credit amount."),
                    memo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false, comment: "Voucher line memo."),
                    journal_voucher_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning journal voucher id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_voucher_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_journal_voucher_lines_journal_vouchers_journal_voucher_id",
                        column: x => x.journal_voucher_id,
                        principalSchema: "erp",
                        principalTable: "journal_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP journal voucher debit and credit lines.");

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Purchase order line id."),
                    line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Purchase order line number."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData unit of measure code."),
                    ordered_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Ordered quantity."),
                    received_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "ERP recorded receipt quantity."),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Purchase unit price."),
                    promised_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Promised receipt date."),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning purchase order id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_order_lines_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalSchema: "erp",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP purchase order lines and received quantity fact.");

            migrationBuilder.CreateTable(
                name: "purchase_receipt_lines",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Purchase receipt line id."),
                    purchase_order_line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Referenced purchase order line number."),
                    received_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Received quantity."),
                    quality_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Line quality status."),
                    purchase_receipt_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning purchase receipt id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_receipt_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_receipt_lines_purchase_receipts_purchase_receipt_id",
                        column: x => x.purchase_receipt_id,
                        principalSchema: "erp",
                        principalTable: "purchase_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP purchase receipt lines.");

            migrationBuilder.CreateTable(
                name: "quotation_lines",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Quotation line id."),
                    line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Quotation line number."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData UOM code."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Quoted quantity."),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Quoted unit price."),
                    required_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Customer required date."),
                    quotation_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning quotation id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotation_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_quotation_lines_quotations_quotation_id",
                        column: x => x.quotation_id,
                        principalSchema: "erp",
                        principalTable: "quotations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP customer quotation lines.");

            migrationBuilder.CreateTable(
                name: "request_for_quotation_lines",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "RFQ line id."),
                    line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "RFQ line number."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData unit of measure code."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Requested quotation quantity."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code."),
                    required_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Required date."),
                    request_for_quotation_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning RFQ id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_for_quotation_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_request_for_quotation_lines_request_for_quotations_request_~",
                        column: x => x.request_for_quotation_id,
                        principalSchema: "erp",
                        principalTable: "request_for_quotations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP request for quotation lines.");

            migrationBuilder.CreateTable(
                name: "request_for_quotation_suppliers",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "RFQ supplier row id.")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    supplier_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData supplier code."),
                    request_for_quotation_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning RFQ id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_for_quotation_suppliers", x => x.id);
                    table.ForeignKey(
                        name: "FK_request_for_quotation_suppliers_request_for_quotations_requ~",
                        column: x => x.request_for_quotation_id,
                        principalSchema: "erp",
                        principalTable: "request_for_quotations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP RFQ invited supplier references.");

            migrationBuilder.CreateTable(
                name: "sales_order_lines",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Sales order line id."),
                    line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Sales order line number."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData UOM code."),
                    ordered_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Ordered quantity."),
                    delivered_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Released delivery quantity."),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Sales unit price."),
                    required_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Customer required date."),
                    sales_order_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning sales order id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_sales_order_lines_sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalSchema: "erp",
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP sales order lines and delivered quantity fact.");

            migrationBuilder.CreateTable(
                name: "supplier_quotation_lines",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Supplier quotation line id."),
                    line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Quotation line number."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData unit of measure code."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Quoted quantity."),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Quoted unit price."),
                    promised_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Supplier promised date."),
                    supplier_quotation_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning supplier quotation id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_quotation_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_supplier_quotation_lines_supplier_quotations_supplier_quota~",
                        column: x => x.supplier_quotation_id,
                        principalSchema: "erp",
                        principalTable: "supplier_quotations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ERP supplier quotation lines.");

            migrationBuilder.CreateIndex(
                name: "IX_account_payables_organization_id_environment_id_payable_no",
                schema: "erp",
                table: "account_payables",
                columns: new[] { "organization_id", "environment_id", "payable_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_receivables_organization_id_environment_id_receivab~",
                schema: "erp",
                table: "account_receivables",
                columns: new[] { "organization_id", "environment_id", "receivable_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName",
                schema: "erp",
                table: "cap_published_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName",
                schema: "erp",
                table: "cap_published_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName1",
                schema: "erp",
                table: "cap_received_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName1",
                schema: "erp",
                table: "cap_received_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_cost_candidates_organization_id_environment_id_candidate_no",
                schema: "erp",
                table: "cost_candidates",
                columns: new[] { "organization_id", "environment_id", "candidate_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_order_lines_delivery_order_id",
                schema: "erp",
                table: "delivery_order_lines",
                column: "delivery_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_orders_organization_id_environment_id_delivery_ord~",
                schema: "erp",
                table: "delivery_orders",
                columns: new[] { "organization_id", "environment_id", "delivery_order_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_voucher_lines_journal_voucher_id",
                schema: "erp",
                table: "journal_voucher_lines",
                column: "journal_voucher_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_vouchers_organization_id_environment_id_voucher_no",
                schema: "erp",
                table: "journal_vouchers",
                columns: new[] { "organization_id", "environment_id", "voucher_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_organization_id_environment_id_opportunity_no",
                schema: "erp",
                table: "opportunities",
                columns: new[] { "organization_id", "environment_id", "opportunity_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_lines_purchase_order_id",
                schema: "erp",
                table: "purchase_order_lines",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_organization_id_environment_id_purchase_ord~",
                schema: "erp",
                table: "purchase_orders",
                columns: new[] { "organization_id", "environment_id", "purchase_order_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_receipt_lines_purchase_receipt_id",
                schema: "erp",
                table: "purchase_receipt_lines",
                column: "purchase_receipt_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_receipts_organization_id_environment_id_purchase_r~",
                schema: "erp",
                table: "purchase_receipts",
                columns: new[] { "organization_id", "environment_id", "purchase_receipt_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_requisitions_organization_id_environment_id_requis~",
                schema: "erp",
                table: "purchase_requisitions",
                columns: new[] { "organization_id", "environment_id", "requisition_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_requisitions_organization_id_environment_id_sugges~",
                schema: "erp",
                table: "purchase_requisitions",
                columns: new[] { "organization_id", "environment_id", "suggestion_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotation_lines_quotation_id",
                schema: "erp",
                table: "quotation_lines",
                column: "quotation_id");

            migrationBuilder.CreateIndex(
                name: "IX_quotations_organization_id_environment_id_quotation_no",
                schema: "erp",
                table: "quotations",
                columns: new[] { "organization_id", "environment_id", "quotation_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_request_for_quotation_lines_request_for_quotation_id",
                schema: "erp",
                table: "request_for_quotation_lines",
                column: "request_for_quotation_id");

            migrationBuilder.CreateIndex(
                name: "IX_request_for_quotation_suppliers_request_for_quotation_id",
                schema: "erp",
                table: "request_for_quotation_suppliers",
                column: "request_for_quotation_id");

            migrationBuilder.CreateIndex(
                name: "IX_request_for_quotations_organization_id_environment_id_rfq_no",
                schema: "erp",
                table: "request_for_quotations",
                columns: new[] { "organization_id", "environment_id", "rfq_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_order_lines_sales_order_id",
                schema: "erp",
                table: "sales_order_lines",
                column: "sales_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_orders_organization_id_environment_id_sales_order_no",
                schema: "erp",
                table: "sales_orders",
                columns: new[] { "organization_id", "environment_id", "sales_order_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_quotation_lines_supplier_quotation_id",
                schema: "erp",
                table: "supplier_quotation_lines",
                column: "supplier_quotation_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_quotations_organization_id_environment_id_quotatio~",
                schema: "erp",
                table: "supplier_quotations",
                columns: new[] { "organization_id", "environment_id", "quotation_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_payables",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "account_receivables",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "cap_locks",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "cap_published_messages",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "cap_received_messages",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "cost_candidates",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "delivery_order_lines",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "journal_voucher_lines",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "opportunities",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "purchase_order_lines",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "purchase_receipt_lines",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "purchase_requisitions",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "quotation_lines",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "request_for_quotation_lines",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "request_for_quotation_suppliers",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "sales_order_lines",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "supplier_quotation_lines",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "delivery_orders",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "journal_vouchers",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "purchase_orders",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "purchase_receipts",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "quotations",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "request_for_quotations",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "sales_orders",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "supplier_quotations",
                schema: "erp");
        }
    }
}
