using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesBusinessLoopFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                schema: "mes",
                table: "work_orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Reason code or text for cancelling the work order.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at_utc",
                schema: "mes",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the completed work order was closed.");

            migrationBuilder.AddColumn<decimal>(
                name: "completed_quantity",
                schema: "mes",
                table: "work_orders",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Cumulative good production quantity reported against the work order.");

            migrationBuilder.AddColumn<string>(
                name: "hold_reason",
                schema: "mes",
                table: "work_orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Reason code or text for holding the work order.");

            migrationBuilder.AddColumn<decimal>(
                name: "over_receipt_tolerance_percent",
                schema: "mes",
                table: "work_orders",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                comment: "Allowed over-production tolerance percentage for cumulative reported quantity.");

            migrationBuilder.AddColumn<decimal>(
                name: "scrap_quantity",
                schema: "mes",
                table: "work_orders",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Cumulative scrap quantity reported against the work order.");

            migrationBuilder.AddColumn<string>(
                name: "defect_record_no",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional MES defect record number linked to this production report.");

            migrationBuilder.AddColumn<string>(
                name: "produced_lot_no",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional produced finished-goods lot number for genealogy.");

            migrationBuilder.AddColumn<decimal>(
                name: "rework_quantity",
                schema: "mes",
                table: "production_reports",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Quantity reported as rework for the operation.");

            migrationBuilder.AddColumn<string>(
                name: "scrap_reason_code",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional scrap reason code linked to reported scrap quantity.");

            migrationBuilder.AddColumn<string>(
                name: "serial_no",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional produced serial number for genealogy.");

            migrationBuilder.AddColumn<string>(
                name: "uom_code",
                schema: "mes",
                table: "production_report_material_consumptions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "UNSPECIFIED",
                comment: "Unit of measure code copied from the line-side material issue request.");

            migrationBuilder.AddColumn<string>(
                name: "uom_code",
                schema: "mes",
                table: "material_issue_requests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "UNSPECIFIED",
                comment: "Unit of measure code captured for the material issue quantity.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "posted_at_utc",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when Inventory posted the receipt movement.");

            migrationBuilder.AddColumn<string>(
                name: "posted_inventory_movement_id",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Inventory movement id posted for this receipt request when known.");

            migrationBuilder.AddColumn<string>(
                name: "produced_lot_no",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional produced finished-goods lot number requested for receipt.");

            migrationBuilder.AddColumn<string>(
                name: "serial_no",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional produced serial number requested for receipt.");

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Requested",
                comment: "MES finished-goods receipt request lifecycle status.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at_utc",
                schema: "mes",
                table: "defect_records",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the MES defect was closed by non-rework disposition.");

            migrationBuilder.AddColumn<string>(
                name: "disposition_reference_id",
                schema: "mes",
                table: "defect_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Downstream disposition reference such as rework work order, scrap movement or return document.");

            migrationBuilder.AddColumn<string>(
                name: "disposition_type",
                schema: "mes",
                table: "defect_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Quality disposition type accepted for this MES defect.");

            migrationBuilder.AddColumn<string>(
                name: "ncr_code",
                schema: "mes",
                table: "defect_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Quality NCR business code linked to this MES defect when disposition is known.");

            migrationBuilder.AddColumn<string>(
                name: "ncr_id",
                schema: "mes",
                table: "defect_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Quality NCR public id linked to this MES defect when disposition is known.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancel_reason",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "closed_at_utc",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "completed_quantity",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "hold_reason",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "over_receipt_tolerance_percent",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "scrap_quantity",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "defect_record_no",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "produced_lot_no",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "rework_quantity",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "scrap_reason_code",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "serial_no",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "uom_code",
                schema: "mes",
                table: "production_report_material_consumptions");

            migrationBuilder.DropColumn(
                name: "uom_code",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "posted_at_utc",
                schema: "mes",
                table: "finished_goods_receipt_requests");

            migrationBuilder.DropColumn(
                name: "posted_inventory_movement_id",
                schema: "mes",
                table: "finished_goods_receipt_requests");

            migrationBuilder.DropColumn(
                name: "produced_lot_no",
                schema: "mes",
                table: "finished_goods_receipt_requests");

            migrationBuilder.DropColumn(
                name: "serial_no",
                schema: "mes",
                table: "finished_goods_receipt_requests");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "mes",
                table: "finished_goods_receipt_requests");

            migrationBuilder.DropColumn(
                name: "closed_at_utc",
                schema: "mes",
                table: "defect_records");

            migrationBuilder.DropColumn(
                name: "disposition_reference_id",
                schema: "mes",
                table: "defect_records");

            migrationBuilder.DropColumn(
                name: "disposition_type",
                schema: "mes",
                table: "defect_records");

            migrationBuilder.DropColumn(
                name: "ncr_code",
                schema: "mes",
                table: "defect_records");

            migrationBuilder.DropColumn(
                name: "ncr_id",
                schema: "mes",
                table: "defect_records");
        }
    }
}
