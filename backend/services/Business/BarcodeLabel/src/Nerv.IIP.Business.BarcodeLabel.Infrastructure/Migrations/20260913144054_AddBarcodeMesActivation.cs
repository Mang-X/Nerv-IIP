using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBarcodeMesActivation : Migration
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
                comment: "Truthful print batch lifecycle status: reserved, ready-to-print, sent-to-printer, delivery-unknown, printed or failed.",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldComment: "Truthful print batch lifecycle status: pending, sent-to-printer, printed or failed.");

            migrationBuilder.AddColumn<string>(
                name: "production_report_id",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "MES production report id that activated this reserved print batch.");

            migrationBuilder.AddColumn<string>(
                name: "production_report_no",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "MES production report number that activated this reserved print batch.");

            migrationBuilder.Sql("UPDATE barcode.label_print_batches SET status = 'ready-to-print' WHERE status = 'pending';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "production_report_id",
                schema: "barcode",
                table: "label_print_batches");

            migrationBuilder.DropColumn(
                name: "production_report_no",
                schema: "barcode",
                table: "label_print_batches");

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
                oldComment: "Truthful print batch lifecycle status: reserved, ready-to-print, sent-to-printer, delivery-unknown, printed or failed.");
        }
    }
}
