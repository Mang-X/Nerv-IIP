using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddErpPurchaseOrderApprovalChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approval_chain_id",
                schema: "erp",
                table: "purchase_orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "BusinessApproval chain id that gates purchase order release.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approval_chain_id",
                schema: "erp",
                table: "purchase_orders");
        }
    }
}
