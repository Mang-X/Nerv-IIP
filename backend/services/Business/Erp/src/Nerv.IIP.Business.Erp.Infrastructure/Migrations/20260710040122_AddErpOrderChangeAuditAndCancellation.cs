using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddErpOrderChangeAuditAndCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "erp",
                table: "sales_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Monotonic sales order revision number.");

            migrationBuilder.AddColumn<bool>(
                name: "cancelled",
                schema: "erp",
                table: "sales_order_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether the unfulfilled sales order line was cancelled.");

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "erp",
                table: "purchase_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Monotonic purchase order revision number.");

            migrationBuilder.CreateTable(
                name: "purchase_order_changes",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "Purchase order change audit row id.")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    change_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Change category: amend, final-delivery, or cancel."),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "Business reason for the order change."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Approval or application status for the purchase order change."),
                    approval_chain_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "BusinessApproval chain id for a pending purchase order amendment."),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the change was requested."),
                    resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the change was approved, rejected, or applied."),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning purchase order id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_changes", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_order_changes_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalSchema: "erp",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Auditable purchase order amendment, final-delivery, and cancellation records.");

            migrationBuilder.CreateTable(
                name: "sales_order_changes",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "Sales order change audit row id.")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    change_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Change category: amend, cancel-line, or cancel."),
                    line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional sales order line number affected by the change."),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "Business reason for the sales order change."),
                    changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the sales order change was applied."),
                    sales_order_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning sales order id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_order_changes", x => x.id);
                    table.ForeignKey(
                        name: "FK_sales_order_changes_sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalSchema: "erp",
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Auditable sales order amendment and cancellation records.");

            migrationBuilder.CreateTable(
                name: "purchase_order_change_lines",
                schema: "erp",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "Purchase order change line audit row id.")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Purchase order line number being changed."),
                    ordered_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Approved target ordered quantity."),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Approved target unit price."),
                    promised_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Approved target promised receipt date."),
                    purchase_order_change_id = table.Column<long>(type: "bigint", nullable: false, comment: "Owning purchase order change audit row id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_change_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_order_change_lines_purchase_order_changes_purchase~",
                        column: x => x.purchase_order_change_id,
                        principalSchema: "erp",
                        principalTable: "purchase_order_changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Auditable target values for a purchase order line amendment.");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_change_lines_purchase_order_change_id",
                schema: "erp",
                table: "purchase_order_change_lines",
                column: "purchase_order_change_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_changes_purchase_order_id_approval_chain_id",
                schema: "erp",
                table: "purchase_order_changes",
                columns: new[] { "purchase_order_id", "approval_chain_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_order_changes_sales_order_id",
                schema: "erp",
                table: "sales_order_changes",
                column: "sales_order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_order_change_lines",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "sales_order_changes",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "purchase_order_changes",
                schema: "erp");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "cancelled",
                schema: "erp",
                table: "sales_order_lines");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "erp",
                table: "purchase_orders");
        }
    }
}
