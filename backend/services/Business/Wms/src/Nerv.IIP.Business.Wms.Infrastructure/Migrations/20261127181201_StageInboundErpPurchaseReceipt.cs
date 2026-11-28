using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StageInboundErpPurchaseReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "receipt_request_fingerprint",
                schema: "wms",
                table: "inbound_orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "SHA-256 fingerprint of the frozen ERP receipt request.");

            migrationBuilder.AddColumn<string>(
                name: "receipt_request_key",
                schema: "wms",
                table: "inbound_orders",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Normalized idempotency key of the frozen ERP receipt request.");

            migrationBuilder.AddColumn<decimal>(
                name: "requested_receipt_quantity",
                schema: "wms",
                table: "inbound_order_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Quantity frozen in the ERP receipt request before acceptance.");

            migrationBuilder.Sql("""
                UPDATE wms.inbound_order_lines AS line
                SET requested_receipt_quantity = line.received_quantity,
                    received_quantity = 0
                FROM wms.inbound_orders AS inbound
                WHERE inbound.id = line.inbound_order_id
                    AND inbound.receipt_batch_id IS NOT NULL
                    AND inbound.erp_purchase_receipt_no IS NULL
                    AND inbound.receipt_confirmation_key IS NOT NULL;

                UPDATE wms.inbound_orders
                SET receipt_request_key = receipt_confirmation_key,
                    receipt_request_fingerprint = receipt_confirmation_fingerprint,
                    receipt_confirmation_key = NULL,
                    receipt_confirmation_fingerprint = NULL
                WHERE receipt_batch_id IS NOT NULL
                    AND erp_purchase_receipt_no IS NULL
                    AND receipt_confirmation_key IS NOT NULL;

                UPDATE wms.inbound_orders AS inbound
                SET status = 'Cancelled',
                    cancellation_reason = cancellation.reason,
                    cancelled_at_utc = cancellation.cancelled_at_utc,
                    version = inbound.version + 1
                FROM wms.purchase_order_cancellations AS cancellation
                WHERE inbound.organization_id = cancellation.organization_id
                    AND inbound.environment_id = cancellation.environment_id
                    AND inbound.inbound_order_no = cancellation.purchase_order_no
                    AND inbound.source_document_type = 'purchase-order'
                    AND inbound.source_document_id = cancellation.purchase_order_no
                    AND inbound.site_code = cancellation.site_code
                    AND inbound.status = 'Open'
                    AND inbound.receipt_request_key IS NOT NULL
                    AND inbound.erp_purchase_receipt_no IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE wms.inbound_order_lines AS line
                SET received_quantity = line.requested_receipt_quantity
                FROM wms.inbound_orders AS inbound
                WHERE inbound.id = line.inbound_order_id
                    AND inbound.receipt_request_key IS NOT NULL
                    AND inbound.erp_purchase_receipt_no IS NULL
                    AND line.requested_receipt_quantity IS NOT NULL;

                UPDATE wms.inbound_orders
                SET receipt_confirmation_key = receipt_request_key,
                    receipt_confirmation_fingerprint = receipt_request_fingerprint
                WHERE receipt_request_key IS NOT NULL
                    AND erp_purchase_receipt_no IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "receipt_request_fingerprint",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropColumn(
                name: "receipt_request_key",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropColumn(
                name: "requested_receipt_quantity",
                schema: "wms",
                table: "inbound_order_lines");
        }
    }
}
