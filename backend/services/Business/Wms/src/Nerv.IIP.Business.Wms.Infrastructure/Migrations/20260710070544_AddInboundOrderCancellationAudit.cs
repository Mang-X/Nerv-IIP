using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundOrderCancellationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                schema: "wms",
                table: "inbound_orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "Auditable reason supplied when the inbound expectation was cancelled.");

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_at_utc",
                schema: "wms",
                table: "inbound_orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the open inbound expectation was cancelled.");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_orders_source_status",
                schema: "wms",
                table: "inbound_orders",
                columns: new[] { "organization_id", "environment_id", "source_document_type", "source_document_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inbound_orders_source_status",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropColumn(
                name: "cancelled_at_utc",
                schema: "wms",
                table: "inbound_orders");
        }
    }
}
