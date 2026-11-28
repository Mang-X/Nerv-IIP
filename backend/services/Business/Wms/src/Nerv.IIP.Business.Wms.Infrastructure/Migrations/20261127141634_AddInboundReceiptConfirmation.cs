using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundReceiptConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "receipt_confirmation_fingerprint",
                schema: "wms",
                table: "inbound_orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "SHA-256 fingerprint of the receipt confirmation intent.");

            migrationBuilder.AddColumn<string>(
                name: "receipt_confirmation_key",
                schema: "wms",
                table: "inbound_orders",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Normalized idempotency key for confirming actual receipt quantities.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "receipt_confirmation_fingerprint",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropColumn(
                name: "receipt_confirmation_key",
                schema: "wms",
                table: "inbound_orders");
        }
    }
}
