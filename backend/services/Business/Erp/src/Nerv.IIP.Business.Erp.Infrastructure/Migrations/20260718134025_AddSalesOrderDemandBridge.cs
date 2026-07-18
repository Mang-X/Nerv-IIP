using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderDemandBridge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "site_code",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "UNSPECIFIED",
                comment: "MasterData site code governing sales-order demand fulfillment.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "site_code",
                schema: "erp",
                table: "sales_orders");
        }
    }
}
