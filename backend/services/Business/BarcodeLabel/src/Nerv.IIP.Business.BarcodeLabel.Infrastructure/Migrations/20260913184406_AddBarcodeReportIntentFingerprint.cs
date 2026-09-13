using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBarcodeReportIntentFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "report_intent_fingerprint",
                schema: "barcode",
                table: "label_print_batches",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                comment: "Optional opaque canonical report-intent fingerprint supplied by the authorized caller; null denotes a general non-MES label intent.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "report_intent_fingerprint",
                schema: "barcode",
                table: "label_print_batches");
        }
    }
}
