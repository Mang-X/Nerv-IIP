using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BindInboundErpPurchaseReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "erp_purchase_receipt_no",
                schema: "wms",
                table: "inbound_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Scoped ERP receipt number returned by committed batch acceptance.");

            migrationBuilder.AddColumn<long>(
                name: "erp_receipt_accepted_version",
                schema: "wms",
                table: "inbound_orders",
                type: "bigint",
                nullable: true,
                comment: "Inbound version returned by the original successful ERP receipt binding.");

            migrationBuilder.AddColumn<Guid>(
                name: "receipt_batch_id",
                schema: "wms",
                table: "inbound_orders",
                type: "uuid",
                nullable: true,
                comment: "Stable ERP acceptance identity for an explicitly confirmed purchase order receipt.");

            migrationBuilder.AddColumn<decimal>(
                name: "receipt_exchange_rate",
                schema: "wms",
                table: "inbound_orders",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: true,
                comment: "Exchange rate frozen when the ERP acceptance batch is staged.");

            migrationBuilder.CreateIndex(
                name: "IX_inbound_orders_organization_id_environment_id_receipt_batch~",
                schema: "wms",
                table: "inbound_orders",
                columns: new[] { "organization_id", "environment_id", "receipt_batch_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inbound_orders_organization_id_environment_id_receipt_batch~",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropColumn(
                name: "erp_purchase_receipt_no",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropColumn(
                name: "erp_receipt_accepted_version",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropColumn(
                name: "receipt_batch_id",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropColumn(
                name: "receipt_exchange_rate",
                schema: "wms",
                table: "inbound_orders");
        }
    }
}
