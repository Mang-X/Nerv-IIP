using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScanNaturalKeyAndDownstreamStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_scan_records_accepted_scanned_value",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.AddColumn<string>(
                name: "downstream_processing_status",
                schema: "barcode",
                table: "scan_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "not-required",
                comment: "Downstream processing status for the scan, such as observed, requested or not-required.");

            migrationBuilder.Sql(
                """
                UPDATE barcode.scan_records
                SET downstream_processing_status = CASE
                    WHEN result = 'rejected' THEN 'not-required'
                    WHEN business_action = 'inventory-movement-requested' THEN 'requested'
                    ELSE 'observed'
                END
                """);

            migrationBuilder.CreateIndex(
                name: "UX_scan_records_accepted_scan_natural_key",
                schema: "barcode",
                table: "scan_records",
                columns: new[] { "organization_id", "environment_id", "scanned_value", "source_workflow", "source_document_id" },
                unique: true,
                filter: "result = 'accepted'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_scan_records_accepted_scan_natural_key",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "downstream_processing_status",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.CreateIndex(
                name: "UX_scan_records_accepted_scanned_value",
                schema: "barcode",
                table: "scan_records",
                columns: new[] { "organization_id", "environment_id", "scanned_value" },
                unique: true,
                filter: "result = 'accepted'");
        }
    }
}
