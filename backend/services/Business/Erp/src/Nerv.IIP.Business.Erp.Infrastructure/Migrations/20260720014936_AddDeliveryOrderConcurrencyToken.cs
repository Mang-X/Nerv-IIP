using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryOrderConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "erp",
                table: "delivery_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Optimistic concurrency token for cumulative WMS delivery projection updates.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                schema: "erp",
                table: "delivery_orders");
        }
    }
}
