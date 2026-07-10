using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintLifecycleAndPrinterTransport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "consumed_at_utc",
                schema: "barcode",
                table: "label_print_items",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when a printed label was accepted by scanner consumption.");

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "barcode",
                table: "label_print_items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "created",
                comment: "Label lifecycle status: created, printed, reprinted, voided or consumed.");

            migrationBuilder.AddColumn<string>(
                name: "void_reason",
                schema: "barcode",
                table: "label_print_items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Reason captured when the label is voided.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "voided_at_utc",
                schema: "barcode",
                table: "label_print_items",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the label became unusable.");

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
                oldComment: "Print batch status such as completed.");

            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Latest printer transport or device failure reason.");

            migrationBuilder.AddColumn<string>(
                name: "print_job_id",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Printer or transport job identifier for the latest attempt.");

            migrationBuilder.AddColumn<string>(
                name: "printer_id",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Configured printer identity selected for the transport attempt.");

            migrationBuilder.Sql("UPDATE barcode.label_print_batches SET status = 'pending' WHERE status = 'completed';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "consumed_at_utc",
                schema: "barcode",
                table: "label_print_items");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "barcode",
                table: "label_print_items");

            migrationBuilder.DropColumn(
                name: "void_reason",
                schema: "barcode",
                table: "label_print_items");

            migrationBuilder.DropColumn(
                name: "voided_at_utc",
                schema: "barcode",
                table: "label_print_items");

            migrationBuilder.DropColumn(
                name: "failure_reason",
                schema: "barcode",
                table: "label_print_batches");

            migrationBuilder.DropColumn(
                name: "print_job_id",
                schema: "barcode",
                table: "label_print_batches");

            migrationBuilder.DropColumn(
                name: "printer_id",
                schema: "barcode",
                table: "label_print_batches");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                comment: "Print batch status such as completed.",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldComment: "Truthful print batch lifecycle status: pending, sent-to-printer, printed or failed.");
        }
    }
}
