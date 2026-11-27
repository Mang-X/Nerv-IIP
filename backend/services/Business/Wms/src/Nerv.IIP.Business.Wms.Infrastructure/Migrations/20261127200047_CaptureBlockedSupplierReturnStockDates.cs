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

            migrationBuilder.AddColumn<bool>(
                name: "match_receipt_stock_dates",
                schema: "wms",
                table: "outbound_order_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Use the exact posted dates for an authoritative rejected receipt return.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "production_date",
                schema: "wms",
                table: "outbound_order_lines",
                type: "date",
                nullable: true,
                comment: "Production date of the original stock posted for a blocked supplier return.");

            migrationBuilder.AddColumn<bool>(
                name: "match_receipt_stock_dates",
                schema: "wms",
                table: "inventory_movement_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Use exact stock dates for an authoritative rejected receipt or its return.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "posted_expiry_date",
                schema: "wms",
                table: "inventory_movement_requests",
                type: "date",
                nullable: true,
                comment: "Expiry date acknowledged by Inventory for the blocked inbound movement.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "posted_production_date",
                schema: "wms",
                table: "inventory_movement_requests",
                type: "date",
                nullable: true,
                comment: "Production date acknowledged by Inventory for the blocked inbound movement.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "wms",
                table: "outbound_order_lines");

            migrationBuilder.DropColumn(
                name: "match_receipt_stock_dates",
                schema: "wms",
                table: "outbound_order_lines");

            migrationBuilder.DropColumn(
                name: "production_date",
                schema: "wms",
                table: "outbound_order_lines");

            migrationBuilder.DropColumn(
                name: "match_receipt_stock_dates",
                schema: "wms",
                table: "inventory_movement_requests");

            migrationBuilder.DropColumn(
                name: "posted_expiry_date",
                schema: "wms",
                table: "inventory_movement_requests");

            migrationBuilder.DropColumn(
                name: "posted_production_date",
                schema: "wms",
                table: "inventory_movement_requests");
        }
    }
}
