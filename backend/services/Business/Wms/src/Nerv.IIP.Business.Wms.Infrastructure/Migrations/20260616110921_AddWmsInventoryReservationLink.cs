using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWmsInventoryReservationLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "inventory_reservation_id",
                schema: "wms",
                table: "outbound_order_lines",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Public Inventory reservation id allocated for this outbound line.");

            migrationBuilder.AddColumn<string>(
                name: "inventory_reservation_id",
                schema: "wms",
                table: "inventory_movement_requests",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional Inventory reservation id used to allocate outbound stock.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "inventory_reservation_id",
                schema: "wms",
                table: "outbound_order_lines");

            migrationBuilder.DropColumn(
                name: "inventory_reservation_id",
                schema: "wms",
                table: "inventory_movement_requests");
        }
    }
}
