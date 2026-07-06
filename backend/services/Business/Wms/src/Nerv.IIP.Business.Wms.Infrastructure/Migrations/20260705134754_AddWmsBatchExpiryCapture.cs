using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsBatchExpiryCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "wms",
                table: "inventory_movement_requests",
                type: "date",
                nullable: true,
                comment: "Optional expiry date carried to Inventory for FEFO-managed batches.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "production_date",
                schema: "wms",
                table: "inventory_movement_requests",
                type: "date",
                nullable: true,
                comment: "Optional production date carried to Inventory for inbound postings.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "wms",
                table: "integration_event_dead_letters",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Dead-letter status: Pending, Replayed, Failed, or Ignored.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Dead-letter status: Pending or Replayed.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "wms",
                table: "inbound_order_lines",
                type: "date",
                nullable: true,
                comment: "Optional received batch expiry date captured by WMS.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "production_date",
                schema: "wms",
                table: "inbound_order_lines",
                type: "date",
                nullable: true,
                comment: "Optional received batch production date captured by WMS.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "wms",
                table: "inventory_movement_requests");

            migrationBuilder.DropColumn(
                name: "production_date",
                schema: "wms",
                table: "inventory_movement_requests");

            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "wms",
                table: "inbound_order_lines");

            migrationBuilder.DropColumn(
                name: "production_date",
                schema: "wms",
                table: "inbound_order_lines");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "wms",
                table: "integration_event_dead_letters",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Dead-letter status: Pending or Replayed.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Dead-letter status: Pending, Replayed, Failed, or Ignored.");
        }
    }
}
