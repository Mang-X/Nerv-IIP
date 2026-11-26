using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSparePartInventoryPostingFailure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "inventory_posting_failure_reason",
                schema: "maintenance",
                table: "maintenance_work_order_spare_part_lines",
                type: "text",
                nullable: true,
                comment: "Inventory rejection reason for the maintenance issue.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "inventory_posting_failure_reason",
                schema: "maintenance",
                table: "maintenance_work_order_spare_part_lines");
        }
    }
}
