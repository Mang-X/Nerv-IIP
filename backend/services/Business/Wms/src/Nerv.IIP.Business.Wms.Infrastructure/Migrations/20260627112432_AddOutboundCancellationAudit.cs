using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundCancellationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                schema: "wms",
                table: "outbound_orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "Outbound cancellation reason for audit.");

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_at_utc",
                schema: "wms",
                table: "outbound_orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC cancellation time.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                schema: "wms",
                table: "outbound_orders");

            migrationBuilder.DropColumn(
                name: "cancelled_at_utc",
                schema: "wms",
                table: "outbound_orders");
        }
    }
}
