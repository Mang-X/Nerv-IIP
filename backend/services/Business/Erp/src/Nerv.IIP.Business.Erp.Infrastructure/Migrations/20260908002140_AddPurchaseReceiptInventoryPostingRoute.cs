using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReceiptInventoryPostingRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "inventory_posting_route",
                schema: "erp",
                table: "purchase_receipts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Direct",
                comment: "Immutable inventory posting owner: Direct ERP request or Wms putaway; legacy receipts remain Direct.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "inventory_posting_route",
                schema: "erp",
                table: "purchase_receipts");
        }
    }
}
