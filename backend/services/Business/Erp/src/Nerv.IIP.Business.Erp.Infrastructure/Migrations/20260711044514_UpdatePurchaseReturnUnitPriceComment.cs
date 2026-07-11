using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePurchaseReturnUnitPriceComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "unit_price",
                schema: "erp",
                table: "purchase_return_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                comment: "Source invoice unit price for debit-note segments or purchase-order unit price for GR/IR segments.",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldComment: "ERP source purchase order unit price.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "unit_price",
                schema: "erp",
                table: "purchase_return_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                comment: "ERP source purchase order unit price.",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldComment: "Source invoice unit price for debit-note segments or purchase-order unit price for GR/IR segments.");
        }
    }
}
