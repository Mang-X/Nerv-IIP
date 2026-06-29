using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Issue556Gs1EpcisAggregationAndSerialUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_scan_records_organization_id_environment_id_gtin_lot_no_ser~",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropIndex(
                name: "IX_scan_records_organization_id_environment_id_scanned_value",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropIndex(
                name: "IX_epcis_events_organization_id_environment_id_gtin_lot_no_ser~",
                schema: "barcode",
                table: "epcis_events");

            migrationBuilder.AddColumn<string>(
                name: "sscc",
                schema: "barcode",
                table: "scan_records",
                type: "character varying(18)",
                maxLength: 18,
                nullable: true,
                comment: "GS1 SSCC-18 logistic unit identifier parsed from AI 00 when present.");

            migrationBuilder.AddColumn<string>(
                name: "parent_epc_uri",
                schema: "barcode",
                table: "epcis_events",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                comment: "Parent EPC URI for aggregation events when a standards-compliant parent URI is available.");

            migrationBuilder.AddColumn<string>(
                name: "parent_sscc",
                schema: "barcode",
                table: "epcis_events",
                type: "character varying(18)",
                maxLength: 18,
                nullable: true,
                comment: "Parent SSCC-18 logistic unit for EPCIS aggregation or disaggregation events.");

            migrationBuilder.CreateIndex(
                name: "IX_scan_records_organization_id_environment_id_epc_uri",
                schema: "barcode",
                table: "scan_records",
                columns: new[] { "organization_id", "environment_id", "epc_uri" },
                unique: true,
                filter: "epc_uri IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_scan_records_organization_id_environment_id_gtin_lot_no_ser~",
                schema: "barcode",
                table: "scan_records",
                columns: new[] { "organization_id", "environment_id", "gtin", "lot_no", "serial_number" },
                unique: true,
                filter: "gtin IS NOT NULL AND serial_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_scan_records_organization_id_environment_id_scanned_value",
                schema: "barcode",
                table: "scan_records",
                columns: new[] { "organization_id", "environment_id", "scanned_value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scan_records_organization_id_environment_id_sscc",
                schema: "barcode",
                table: "scan_records",
                columns: new[] { "organization_id", "environment_id", "sscc" });

            migrationBuilder.CreateIndex(
                name: "IX_epcis_events_organization_id_environment_id_event_type_epc_~",
                schema: "barcode",
                table: "epcis_events",
                columns: new[] { "organization_id", "environment_id", "event_type", "epc_uri" },
                unique: true,
                filter: "epc_uri IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_epcis_events_organization_id_environment_id_event_type_gtin~",
                schema: "barcode",
                table: "epcis_events",
                columns: new[] { "organization_id", "environment_id", "event_type", "gtin", "lot_no", "serial_number" },
                unique: true,
                filter: "gtin IS NOT NULL AND serial_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_epcis_events_organization_id_environment_id_parent_sscc",
                schema: "barcode",
                table: "epcis_events",
                columns: new[] { "organization_id", "environment_id", "parent_sscc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_scan_records_organization_id_environment_id_epc_uri",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropIndex(
                name: "IX_scan_records_organization_id_environment_id_gtin_lot_no_ser~",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropIndex(
                name: "IX_scan_records_organization_id_environment_id_scanned_value",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropIndex(
                name: "IX_scan_records_organization_id_environment_id_sscc",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropIndex(
                name: "IX_epcis_events_organization_id_environment_id_event_type_epc_~",
                schema: "barcode",
                table: "epcis_events");

            migrationBuilder.DropIndex(
                name: "IX_epcis_events_organization_id_environment_id_event_type_gtin~",
                schema: "barcode",
                table: "epcis_events");

            migrationBuilder.DropIndex(
                name: "IX_epcis_events_organization_id_environment_id_parent_sscc",
                schema: "barcode",
                table: "epcis_events");

            migrationBuilder.DropColumn(
                name: "sscc",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "parent_epc_uri",
                schema: "barcode",
                table: "epcis_events");

            migrationBuilder.DropColumn(
                name: "parent_sscc",
                schema: "barcode",
                table: "epcis_events");

            migrationBuilder.CreateIndex(
                name: "IX_scan_records_organization_id_environment_id_gtin_lot_no_ser~",
                schema: "barcode",
                table: "scan_records",
                columns: new[] { "organization_id", "environment_id", "gtin", "lot_no", "serial_number" });

            migrationBuilder.CreateIndex(
                name: "IX_scan_records_organization_id_environment_id_scanned_value",
                schema: "barcode",
                table: "scan_records",
                columns: new[] { "organization_id", "environment_id", "scanned_value" });

            migrationBuilder.CreateIndex(
                name: "IX_epcis_events_organization_id_environment_id_gtin_lot_no_ser~",
                schema: "barcode",
                table: "epcis_events",
                columns: new[] { "organization_id", "environment_id", "gtin", "lot_no", "serial_number" });
        }
    }
}
