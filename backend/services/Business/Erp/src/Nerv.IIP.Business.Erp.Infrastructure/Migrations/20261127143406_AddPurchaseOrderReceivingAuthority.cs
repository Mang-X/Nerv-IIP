using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderReceivingAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "receiving_authority",
                schema: "erp",
                table: "purchase_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Direct",
                comment: "Immutable receiving authority determined when the purchase order is created.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "receiving_authority",
                schema: "erp",
                table: "purchase_orders");
        }
    }
}
