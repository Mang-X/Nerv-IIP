using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CaptureBlockedSupplierReturnStockDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "wms",
                table: "outbound_order_lines",
                type: "date",
                nullable: true,
                comment: "Expiry date of the original stock posted for a blocked supplier return.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "production_date",
                schema: "wms",
                table: "outbound_order_lines",
                type: "date",
                nullable: true,
                comment: "Production date of the original stock posted for a blocked supplier return.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "wms",
                table: "outbound_order_lines");

            migrationBuilder.DropColumn(
                name: "production_date",
                schema: "wms",
                table: "outbound_order_lines");
        }
    }
}
