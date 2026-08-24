using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryUnknownPrintBatchStatusComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                comment: "Truthful print batch lifecycle status: pending, sent-to-printer, delivery-unknown, printed or failed.",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldComment: "Truthful print batch lifecycle status: pending, sent-to-printer, printed or failed.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                comment: "Truthful print batch lifecycle status: pending, sent-to-printer, printed or failed.",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldComment: "Truthful print batch lifecycle status: pending, sent-to-printer, delivery-unknown, printed or failed.");
        }
    }
}
