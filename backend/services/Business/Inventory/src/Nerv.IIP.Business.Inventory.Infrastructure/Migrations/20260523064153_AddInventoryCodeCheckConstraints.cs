using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryCodeCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_movements_location_code_format",
                schema: "inventory",
                table: "stock_movements",
                sql: "location_code ~ '^[A-Za-z0-9_.:-]+$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_movements_site_code_format",
                schema: "inventory",
                table: "stock_movements",
                sql: "site_code ~ '^[A-Za-z0-9_.:-]+$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_movements_sku_code_format",
                schema: "inventory",
                table: "stock_movements",
                sql: "sku_code ~ '^[A-Za-z0-9_.:-]+$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_locations_location_code_format",
                schema: "inventory",
                table: "stock_locations",
                sql: "location_code ~ '^[A-Za-z0-9_.:-]+$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_locations_site_code_format",
                schema: "inventory",
                table: "stock_locations",
                sql: "site_code ~ '^[A-Za-z0-9_.:-]+$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_ledgers_location_code_format",
                schema: "inventory",
                table: "stock_ledgers",
                sql: "location_code ~ '^[A-Za-z0-9_.:-]+$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_ledgers_site_code_format",
                schema: "inventory",
                table: "stock_ledgers",
                sql: "site_code ~ '^[A-Za-z0-9_.:-]+$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_ledgers_sku_code_format",
                schema: "inventory",
                table: "stock_ledgers",
                sql: "sku_code ~ '^[A-Za-z0-9_.:-]+$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_count_tasks_location_code_format",
                schema: "inventory",
                table: "stock_count_tasks",
                sql: "location_code ~ '^[A-Za-z0-9_.:-]+$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_count_tasks_site_code_format",
                schema: "inventory",
                table: "stock_count_tasks",
                sql: "site_code ~ '^[A-Za-z0-9_.:-]+$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_count_tasks_sku_code_format",
                schema: "inventory",
                table: "stock_count_tasks",
                sql: "sku_code ~ '^[A-Za-z0-9_.:-]+$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_count_adjustments_location_code_format",
                schema: "inventory",
                table: "stock_count_adjustments",
                sql: "location_code ~ '^[A-Za-z0-9_.:-]+$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_count_adjustments_site_code_format",
                schema: "inventory",
                table: "stock_count_adjustments",
                sql: "site_code ~ '^[A-Za-z0-9_.:-]+$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_count_adjustments_sku_code_format",
                schema: "inventory",
                table: "stock_count_adjustments",
                sql: "sku_code ~ '^[A-Za-z0-9_.:-]+$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_movements_location_code_format",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_movements_site_code_format",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_movements_sku_code_format",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_locations_location_code_format",
                schema: "inventory",
                table: "stock_locations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_locations_site_code_format",
                schema: "inventory",
                table: "stock_locations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_ledgers_location_code_format",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_ledgers_site_code_format",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_ledgers_sku_code_format",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_count_tasks_location_code_format",
                schema: "inventory",
                table: "stock_count_tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_count_tasks_site_code_format",
                schema: "inventory",
                table: "stock_count_tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_count_tasks_sku_code_format",
                schema: "inventory",
                table: "stock_count_tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_count_adjustments_location_code_format",
                schema: "inventory",
                table: "stock_count_adjustments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_count_adjustments_site_code_format",
                schema: "inventory",
                table: "stock_count_adjustments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_count_adjustments_sku_code_format",
                schema: "inventory",
                table: "stock_count_adjustments");
        }
    }
}
