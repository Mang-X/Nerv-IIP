using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddErpDeliveryShipmentProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "completed_at_utc",
                schema: "erp",
                table: "delivery_orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when cumulative WMS shipment quantities completed every ERP delivery line.");

            migrationBuilder.AddColumn<DateTime>(
                name: "shipped_at_utc",
                schema: "erp",
                table: "delivery_orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the first positive WMS shipment quantity was projected to ERP.");

            migrationBuilder.AddColumn<decimal>(
                name: "shipped_quantity",
                schema: "erp",
                table: "delivery_order_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Cumulative WMS shipped quantity projected idempotently for this delivery line.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "completed_at_utc",
                schema: "erp",
                table: "delivery_orders");

            migrationBuilder.DropColumn(
                name: "shipped_at_utc",
                schema: "erp",
                table: "delivery_orders");

            migrationBuilder.DropColumn(
                name: "shipped_quantity",
                schema: "erp",
                table: "delivery_order_lines");
        }
    }
}
