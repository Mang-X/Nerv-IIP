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
                name: "printer_id",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Configured printer identity for the latest dispatch or reprint transport attempt.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "Configured printer identity selected for the transport attempt.");

            migrationBuilder.AlterColumn<string>(
                name: "print_job_id",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Job identifier for the latest dispatch or reprint transport attempt; it may replace the original whole-batch job.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true,
                oldComment: "Printer or transport job identifier for the latest attempt.");

            migrationBuilder.AlterColumn<string>(
                name: "failure_reason",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Failure reason for the latest dispatch or reprint transport attempt; it is not by itself the whole-batch lifecycle conclusion.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "Latest printer transport or device failure reason.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                comment: "Whole-batch dispatch lifecycle status: pending, sent-to-printer, delivery-unknown, printed or failed; single-item reprint does not change it.",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldComment: "Truthful print batch lifecycle status: pending, sent-to-printer, printed or failed.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "printer_id",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Configured printer identity selected for the transport attempt.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "Configured printer identity for the latest dispatch or reprint transport attempt.");

            migrationBuilder.AlterColumn<string>(
                name: "print_job_id",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Printer or transport job identifier for the latest attempt.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true,
                oldComment: "Job identifier for the latest dispatch or reprint transport attempt; it may replace the original whole-batch job.");

            migrationBuilder.AlterColumn<string>(
                name: "failure_reason",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Latest printer transport or device failure reason.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "Failure reason for the latest dispatch or reprint transport attempt; it is not by itself the whole-batch lifecycle conclusion.");

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
                oldComment: "Whole-batch dispatch lifecycle status: pending, sent-to-printer, delivery-unknown, printed or failed; single-item reprint does not change it.");
        }
    }
}
