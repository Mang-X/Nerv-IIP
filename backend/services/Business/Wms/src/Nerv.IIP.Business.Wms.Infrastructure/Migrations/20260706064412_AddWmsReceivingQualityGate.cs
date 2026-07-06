using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsReceivingQualityGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "inspection_record_id",
                schema: "wms",
                table: "inbound_order_lines",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Quality inspection record id that released or rejected this inbound line.");

            migrationBuilder.AddColumn<string>(
                name: "quality_disposition_reason",
                schema: "wms",
                table: "inbound_order_lines",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "Optional Quality disposition reason copied from the inspection result.");

            migrationBuilder.AddColumn<string>(
                name: "quality_gate_status",
                schema: "wms",
                table: "inbound_order_lines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "not-required",
                comment: "WMS inbound quality gate state: pending, passed, conditional-release, rejected or not-required.");

            migrationBuilder.CreateTable(
                name: "supplier_return_requests",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Supplier return request id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    supplier_return_no = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false, comment: "WMS supplier return request number."),
                    inbound_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source WMS inbound order number."),
                    inbound_order_line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source WMS inbound order line number."),
                    inspection_record_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Quality inspection record that rejected the received stock."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Rejected SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Rejected stock unit of measure."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Site code where rejected stock was received."),
                    location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Rejected stock quarantine or staging location."),
                    lot_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional rejected lot number."),
                    serial_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional rejected serial number."),
                    owner_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Rejected stock owner type."),
                    owner_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional rejected stock owner id."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Rejected quantity to return to supplier."),
                    disposition_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Quality disposition type, currently return-to-supplier."),
                    disposition_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "Quality rejection or disposition reason."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Supplier return request status."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when WMS created the supplier return request.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_return_requests", x => x.id);
                },
                comment: "WMS supplier return request facts generated from rejected receiving inspections.");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_return_requests_organization_id_environment_id_inb~",
                schema: "wms",
                table: "supplier_return_requests",
                columns: new[] { "organization_id", "environment_id", "inbound_order_no", "inbound_order_line_no", "inspection_record_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_return_requests_organization_id_environment_id_sup~",
                schema: "wms",
                table: "supplier_return_requests",
                columns: new[] { "organization_id", "environment_id", "supplier_return_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_return_requests",
                schema: "wms");

            migrationBuilder.DropColumn(
                name: "inspection_record_id",
                schema: "wms",
                table: "inbound_order_lines");

            migrationBuilder.DropColumn(
                name: "quality_disposition_reason",
                schema: "wms",
                table: "inbound_order_lines");

            migrationBuilder.DropColumn(
                name: "quality_gate_status",
                schema: "wms",
                table: "inbound_order_lines");
        }
    }
}
