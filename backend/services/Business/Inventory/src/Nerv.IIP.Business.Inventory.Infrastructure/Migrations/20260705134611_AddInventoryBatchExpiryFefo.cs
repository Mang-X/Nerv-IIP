using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryBatchExpiryFefo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stock_ledgers_organization_id_environment_id_sku_code_uom_c~",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.AlterTable(
                name: "stock_ledgers",
                schema: "inventory",
                comment: "Inventory current stock ledger balances by SKU, UOM, site, location, lot, serial, expiry, quality and owner dimensions.",
                oldComment: "Inventory current stock ledger balances by SKU, UOM, site, location, lot, serial, quality and owner dimensions.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "inventory",
                table: "stock_reservations",
                type: "date",
                nullable: true,
                comment: "Optional batch expiry date reserved for FEFO traceability.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "production_date",
                schema: "inventory",
                table: "stock_reservations",
                type: "date",
                nullable: true,
                comment: "Optional batch production date reserved.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "inventory",
                table: "stock_movements",
                type: "date",
                nullable: true,
                comment: "Optional batch expiry date carried by the movement.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "production_date",
                schema: "inventory",
                table: "stock_movements",
                type: "date",
                nullable: true,
                comment: "Optional batch production date captured with the movement.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "inventory",
                table: "stock_ledgers",
                type: "date",
                nullable: true,
                comment: "Optional batch expiry date used by expiry alerts and FEFO allocation.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "production_date",
                schema: "inventory",
                table: "stock_ledgers",
                type: "date",
                nullable: true,
                comment: "Optional batch production date captured from receipt or production completion.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "inventory",
                table: "integration_event_dead_letters",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Dead-letter status: Pending, Replayed, Failed, or Ignored.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Dead-letter status: Pending or Replayed.");

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_organization_id_environment_id_site_code~",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "organization_id", "environment_id", "site_code", "sku_code", "expiry_date", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_organization_id_environment_id_site_code_sk~",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "organization_id", "environment_id", "site_code", "sku_code", "expiry_date" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_ledgers_organization_id_environment_id_site_code_sku_~",
                schema: "inventory",
                table: "stock_ledgers",
                columns: new[] { "organization_id", "environment_id", "site_code", "sku_code", "expiry_date" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_ledgers_organization_id_environment_id_sku_code_uom_c~",
                schema: "inventory",
                table: "stock_ledgers",
                columns: new[] { "organization_id", "environment_id", "sku_code", "uom_code", "site_code", "location_code", "lot_no", "serial_no", "production_date", "expiry_date", "quality_status", "owner_type", "owner_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stock_reservations_organization_id_environment_id_site_code~",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_organization_id_environment_id_site_code_sk~",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_stock_ledgers_organization_id_environment_id_site_code_sku_~",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.DropIndex(
                name: "IX_stock_ledgers_organization_id_environment_id_sku_code_uom_c~",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "production_date",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "production_date",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.DropColumn(
                name: "production_date",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.AlterTable(
                name: "stock_ledgers",
                schema: "inventory",
                comment: "Inventory current stock ledger balances by SKU, UOM, site, location, lot, serial, quality and owner dimensions.",
                oldComment: "Inventory current stock ledger balances by SKU, UOM, site, location, lot, serial, expiry, quality and owner dimensions.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "inventory",
                table: "integration_event_dead_letters",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Dead-letter status: Pending or Replayed.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Dead-letter status: Pending, Replayed, Failed, or Ignored.");

            migrationBuilder.CreateIndex(
                name: "IX_stock_ledgers_organization_id_environment_id_sku_code_uom_c~",
                schema: "inventory",
                table: "stock_ledgers",
                columns: new[] { "organization_id", "environment_id", "sku_code", "uom_code", "site_code", "location_code", "lot_no", "serial_no", "quality_status", "owner_type", "owner_id" },
                unique: true);
        }
    }
}
