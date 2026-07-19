using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryExpiryProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "expiry_date_source",
                schema: "inventory",
                table: "stock_ledgers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "Persisted expiry provenance: direct, derived, mixed, or null when unknown.");

            migrationBuilder.AddColumn<int>(
                name: "shelf_life_days",
                schema: "inventory",
                table: "stock_ledgers",
                type: "integer",
                nullable: true,
                comment: "Shelf-life days used when the persisted expiry date was derived; null for direct, mixed, or historical provenance.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_ledgers_expiry_date_source",
                schema: "inventory",
                table: "stock_ledgers",
                sql: "expiry_date_source is null or expiry_date_source in ('direct','derived','mixed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_ledgers_expiry_provenance",
                schema: "inventory",
                table: "stock_ledgers",
                sql: "(expiry_date_source is null and shelf_life_days is null) or (expiry_date_source is not null and ((expiry_date_source = 'derived' and production_date is not null and expiry_date is not null and shelf_life_days between 1 and 3660 and expiry_date - production_date = shelf_life_days) or (expiry_date_source in ('direct','mixed') and expiry_date is not null and shelf_life_days is null)))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_ledgers_expiry_date_source",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_ledgers_expiry_provenance",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.DropColumn(
                name: "expiry_date_source",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.DropColumn(
                name: "shelf_life_days",
                schema: "inventory",
                table: "stock_ledgers");
        }
    }
}
