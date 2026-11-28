using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProjectErpReceiptInspection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inbound_order_lines_inbound_order_id",
                schema: "wms",
                table: "inbound_order_lines");

            migrationBuilder.AddColumn<decimal>(
                name: "erp_receipt_inspected_quantity",
                schema: "wms",
                table: "inbound_order_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Quantity covered by the projected Quality decision.");

            migrationBuilder.AddColumn<string>(
                name: "erp_receipt_inspection_reason",
                schema: "wms",
                table: "inbound_order_lines",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Reason supplied by Quality for the projected decision.");

            migrationBuilder.AddColumn<string>(
                name: "erp_receipt_inspection_record_id",
                schema: "wms",
                table: "inbound_order_lines",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "First Quality record projected for the accepted ERP receipt line.");

            migrationBuilder.AddColumn<string>(
                name: "erp_receipt_inspection_result",
                schema: "wms",
                table: "inbound_order_lines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Projected Quality decision, independent of stock disposition.");

            migrationBuilder.CreateIndex(
                name: "IX_inbound_order_lines_inbound_order_id_erp_receipt_inspection~",
                schema: "wms",
                table: "inbound_order_lines",
                columns: new[] { "inbound_order_id", "erp_receipt_inspection_record_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inbound_order_lines_inbound_order_id_erp_receipt_inspection~",
                schema: "wms",
                table: "inbound_order_lines");

            migrationBuilder.DropColumn(
                name: "erp_receipt_inspected_quantity",
                schema: "wms",
                table: "inbound_order_lines");

            migrationBuilder.DropColumn(
                name: "erp_receipt_inspection_reason",
                schema: "wms",
                table: "inbound_order_lines");

            migrationBuilder.DropColumn(
                name: "erp_receipt_inspection_record_id",
                schema: "wms",
                table: "inbound_order_lines");

            migrationBuilder.DropColumn(
                name: "erp_receipt_inspection_result",
                schema: "wms",
                table: "inbound_order_lines");

            migrationBuilder.CreateIndex(
                name: "IX_inbound_order_lines_inbound_order_id",
                schema: "wms",
                table: "inbound_order_lines",
                column: "inbound_order_id");
        }
    }
}
