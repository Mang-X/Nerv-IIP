using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGs1ScanTraceability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "business_action",
                schema: "barcode",
                table: "scan_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Downstream business action selected for the accepted scan.");

            migrationBuilder.AddColumn<string>(
                name: "downstream_event_id",
                schema: "barcode",
                table: "scan_records",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Deterministic downstream event id for idempotent business action routing.");

            migrationBuilder.AddColumn<string>(
                name: "epc_uri",
                schema: "barcode",
                table: "scan_records",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                comment: "EPC URI derived from parsed GTIN and serial number.");

            migrationBuilder.AddColumn<string>(
                name: "gtin",
                schema: "barcode",
                table: "scan_records",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true,
                comment: "GS1 GTIN parsed from an accepted scan value.");

            migrationBuilder.AddColumn<string>(
                name: "location_code",
                schema: "barcode",
                table: "scan_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Inventory or workflow location supplied by scan context.");

            migrationBuilder.AddColumn<string>(
                name: "lot_no",
                schema: "barcode",
                table: "scan_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "GS1 lot or batch parsed from an accepted scan value.");

            migrationBuilder.AddColumn<string>(
                name: "owner_id",
                schema: "barcode",
                table: "scan_records",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional inventory owner id supplied by scan context.");

            migrationBuilder.AddColumn<string>(
                name: "owner_type",
                schema: "barcode",
                table: "scan_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Inventory owner type supplied by scan context.");

            migrationBuilder.AddColumn<string>(
                name: "quality_status",
                schema: "barcode",
                table: "scan_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Quality status supplied by scan context for inventory movement routing.");

            migrationBuilder.AddColumn<decimal>(
                name: "quantity",
                schema: "barcode",
                table: "scan_records",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Quantity parsed from GS1 AI 30 or supplied by the scan workflow.");

            migrationBuilder.AddColumn<string>(
                name: "serial_number",
                schema: "barcode",
                table: "scan_records",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "GS1 serial number parsed from an accepted scan value.");

            migrationBuilder.AddColumn<string>(
                name: "site_code",
                schema: "barcode",
                table: "scan_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Site code supplied by scan context for downstream business action routing.");

            migrationBuilder.AddColumn<string>(
                name: "sku_code",
                schema: "barcode",
                table: "scan_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "SKU code supplied by scan context for downstream business action routing.");

            migrationBuilder.AddColumn<string>(
                name: "uom_code",
                schema: "barcode",
                table: "scan_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Unit of measure supplied by scan context for downstream business action routing.");

            migrationBuilder.AddColumn<string>(
                name: "epc_uri",
                schema: "barcode",
                table: "label_print_items",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                comment: "EPC URI derived from GTIN and serial number for EPCIS traceability.");

            migrationBuilder.AddColumn<string>(
                name: "gtin",
                schema: "barcode",
                table: "label_print_items",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true,
                comment: "Parsed or generated GS1 GTIN including check digit for serialized labels.");

            migrationBuilder.AddColumn<string>(
                name: "lot_no",
                schema: "barcode",
                table: "label_print_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Batch or lot number encoded in the generated GS1 label.");

            migrationBuilder.AddColumn<string>(
                name: "serial_number",
                schema: "barcode",
                table: "label_print_items",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Serialized unit identifier encoded in the generated GS1 label.");

            migrationBuilder.CreateTable(
                name: "epcis_events",
                schema: "barcode",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "EPCIS event id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the EPCIS event."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the EPCIS event occurred."),
                    event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "EPCIS event type such as commissioning or objectEvent."),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "EPCIS action value such as ADD or OBSERVE."),
                    business_step = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business step represented by the EPCIS event."),
                    disposition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Disposition associated with the EPCIS event."),
                    label_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Raw barcode value or generated label value associated with the event."),
                    gtin = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true, comment: "GS1 GTIN associated with the event."),
                    lot_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Lot or batch associated with the event."),
                    serial_number = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Serialized unit associated with the event."),
                    epc_uri = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true, comment: "EPC URI associated with the serialized event."),
                    source_workflow = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source workflow that created the event."),
                    source_document_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Source business document public id associated with the event."),
                    label_print_batch_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Optional label print batch id that owns commissioning events."),
                    label_print_item_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Optional label print item id that caused a commissioning event."),
                    scan_record_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Optional scan record id that caused an object event."),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the EPCIS event occurred.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_epcis_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_epcis_events_label_print_batches_label_print_batch_id",
                        column: x => x.label_print_batch_id,
                        principalSchema: "barcode",
                        principalTable: "label_print_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_epcis_events_scan_records_scan_record_id",
                        column: x => x.scan_record_id,
                        principalSchema: "barcode",
                        principalTable: "scan_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "EPCIS traceability events generated from serialized label commissioning and accepted scans.");

            migrationBuilder.CreateIndex(
                name: "IX_scan_records_organization_id_environment_id_gtin_lot_no_ser~",
                schema: "barcode",
                table: "scan_records",
                columns: new[] { "organization_id", "environment_id", "gtin", "lot_no", "serial_number" });

            migrationBuilder.CreateIndex(
                name: "IX_label_print_items_gtin_lot_no_serial_number",
                schema: "barcode",
                table: "label_print_items",
                columns: new[] { "gtin", "lot_no", "serial_number" });

            migrationBuilder.CreateIndex(
                name: "IX_epcis_events_label_print_batch_id",
                schema: "barcode",
                table: "epcis_events",
                column: "label_print_batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_epcis_events_label_print_item_id",
                schema: "barcode",
                table: "epcis_events",
                column: "label_print_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_epcis_events_organization_id_environment_id_gtin_lot_no_ser~",
                schema: "barcode",
                table: "epcis_events",
                columns: new[] { "organization_id", "environment_id", "gtin", "lot_no", "serial_number" });

            migrationBuilder.CreateIndex(
                name: "IX_epcis_events_organization_id_environment_id_source_workflow~",
                schema: "barcode",
                table: "epcis_events",
                columns: new[] { "organization_id", "environment_id", "source_workflow", "source_document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_epcis_events_scan_record_id",
                schema: "barcode",
                table: "epcis_events",
                column: "scan_record_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "epcis_events",
                schema: "barcode");

            migrationBuilder.DropIndex(
                name: "IX_scan_records_organization_id_environment_id_gtin_lot_no_ser~",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropIndex(
                name: "IX_label_print_items_gtin_lot_no_serial_number",
                schema: "barcode",
                table: "label_print_items");

            migrationBuilder.DropColumn(
                name: "business_action",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "downstream_event_id",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "epc_uri",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "gtin",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "location_code",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "lot_no",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "owner_type",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "quality_status",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "quantity",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "serial_number",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "site_code",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "sku_code",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "uom_code",
                schema: "barcode",
                table: "scan_records");

            migrationBuilder.DropColumn(
                name: "epc_uri",
                schema: "barcode",
                table: "label_print_items");

            migrationBuilder.DropColumn(
                name: "gtin",
                schema: "barcode",
                table: "label_print_items");

            migrationBuilder.DropColumn(
                name: "lot_no",
                schema: "barcode",
                table: "label_print_items");

            migrationBuilder.DropColumn(
                name: "serial_number",
                schema: "barcode",
                table: "label_print_items");
        }
    }
}
