using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinishedGoodsReceiptUnitCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Finished goods unit cost carried to Inventory movement requests for moving-average valuation; legacy rows may be null.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "unit_cost",
                schema: "mes",
                table: "finished_goods_receipt_requests");
        }
    }
}
