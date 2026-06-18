using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryStatusReservationValuation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "quality_status",
                schema: "inventory",
                table: "stock_movements",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Canonical stock status: unrestricted, quality or blocked.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Quality status carried by stock facts.");

            migrationBuilder.AddColumn<decimal>(
                name: "movement_amount",
                schema: "inventory",
                table: "stock_movements",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Signed movement amount derived from quantity and unit cost.");

            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                schema: "inventory",
                table: "stock_movements",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Optional movement unit cost used for moving-average valuation.");

            migrationBuilder.AlterColumn<decimal>(
                name: "reserved_quantity",
                schema: "inventory",
                table: "stock_ledgers",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                comment: "Current reserved stock quantity held by Inventory reservations.",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldComment: "Current reserved quantity; always zero in Inventory MVP.");

            migrationBuilder.AddColumn<string>(
                name: "frozen_count_task_code",
                schema: "inventory",
                table: "stock_ledgers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Open count task code that currently freezes this ledger, when any.");

            migrationBuilder.AddColumn<decimal>(
                name: "inventory_value",
                schema: "inventory",
                table: "stock_ledgers",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Current inventory value for this ledger dimension.");

            migrationBuilder.AddColumn<bool>(
                name: "is_frozen_for_count",
                schema: "inventory",
                table: "stock_ledgers",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Flag indicating regular movements are blocked while an open count task owns this ledger snapshot.");

            migrationBuilder.AddColumn<decimal>(
                name: "moving_average_unit_cost",
                schema: "inventory",
                table: "stock_ledgers",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Current moving-average unit cost for this ledger dimension.");

            migrationBuilder.CreateTable(
                name: "stock_reservations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Stock reservation aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the reservation."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the reservation is valid."),
                    source_service = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source service that requested the reservation."),
                    source_document_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Source document id that owns the reservation."),
                    source_document_line_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Optional source document line id that owns the reservation."),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Reservation idempotency key unique within source document scope."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData unit of measure code."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code."),
                    location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Inventory stock location code."),
                    lot_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional lot or batch number dimension."),
                    serial_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional serial number dimension."),
                    quality_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Canonical stock status reserved: unrestricted, quality or blocked."),
                    owner_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Stock ownership type such as company, customer or supplier."),
                    owner_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional public owner reference id."),
                    reserved_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Original reserved quantity."),
                    released_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Quantity released back to availability."),
                    allocated_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Quantity allocated to outbound consumption."),
                    open_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Remaining reserved quantity not released or allocated."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Reservation lifecycle status."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the reservation was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the reservation was last changed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_reservations", x => x.id);
                    table.CheckConstraint("ck_stock_reservations_location_code_format", "location_code ~ '^[A-Za-z0-9_.:-]+$'");
                    table.CheckConstraint("ck_stock_reservations_quality_status", "quality_status in ('unrestricted','quality','blocked')");
                    table.CheckConstraint("ck_stock_reservations_site_code_format", "site_code ~ '^[A-Za-z0-9_.:-]+$'");
                    table.CheckConstraint("ck_stock_reservations_sku_code_format", "sku_code ~ '^[A-Za-z0-9_.:-]+$'");
                },
                comment: "Inventory stock reservation facts by source document and ledger dimension.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_movements_quality_status",
                schema: "inventory",
                table: "stock_movements",
                sql: "quality_status in ('unrestricted','quality','blocked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_ledgers_quality_status",
                schema: "inventory",
                table: "stock_ledgers",
                sql: "quality_status in ('unrestricted','quality','blocked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_count_tasks_quality_status",
                schema: "inventory",
                table: "stock_count_tasks",
                sql: "quality_status in ('unrestricted','quality','blocked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_count_adjustments_quality_status",
                schema: "inventory",
                table: "stock_count_adjustments",
                sql: "quality_status in ('unrestricted','quality','blocked')");

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_organization_id_environment_id_sku_code_~",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "organization_id", "environment_id", "sku_code", "site_code", "location_code", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_organization_id_environment_id_source_se~",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "organization_id", "environment_id", "source_service", "source_document_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_reservations",
                schema: "inventory");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_movements_quality_status",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_ledgers_quality_status",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_count_tasks_quality_status",
                schema: "inventory",
                table: "stock_count_tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_count_adjustments_quality_status",
                schema: "inventory",
                table: "stock_count_adjustments");

            migrationBuilder.DropColumn(
                name: "movement_amount",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "frozen_count_task_code",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.DropColumn(
                name: "inventory_value",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.DropColumn(
                name: "is_frozen_for_count",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.DropColumn(
                name: "moving_average_unit_cost",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.AlterColumn<string>(
                name: "quality_status",
                schema: "inventory",
                table: "stock_movements",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Quality status carried by stock facts.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Canonical stock status: unrestricted, quality or blocked.");

            migrationBuilder.AlterColumn<decimal>(
                name: "reserved_quantity",
                schema: "inventory",
                table: "stock_ledgers",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                comment: "Current reserved quantity; always zero in Inventory MVP.",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldComment: "Current reserved stock quantity held by Inventory reservations.");
        }
    }
}
