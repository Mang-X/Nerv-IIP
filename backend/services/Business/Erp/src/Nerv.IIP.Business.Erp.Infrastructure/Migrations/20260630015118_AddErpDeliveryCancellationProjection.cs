using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddErpDeliveryCancellationProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                schema: "erp",
                table: "delivery_orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "WMS cancellation reason projected to ERP delivery order.");

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_at_utc",
                schema: "erp",
                table: "delivery_orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when WMS cancellation was projected to ERP.");

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "erp",
                table: "delivery_orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "released",
                comment: "ERP delivery order lifecycle status projected from WMS execution facts.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                schema: "erp",
                table: "delivery_orders");

            migrationBuilder.DropColumn(
                name: "cancelled_at_utc",
                schema: "erp",
                table: "delivery_orders");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "erp",
                table: "delivery_orders");
        }
    }
}
