using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSparePartIssueLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "location_code",
                schema: "maintenance",
                table: "maintenance_work_order_spare_part_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Registered inventory location the spare part is issued from; null only on lines recorded before issue locations were captured.");

            migrationBuilder.AddColumn<string>(
                name: "site_code",
                schema: "maintenance",
                table: "maintenance_work_order_spare_part_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Inventory site the spare part is issued from; null only on lines recorded before issue locations were captured.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "location_code",
                schema: "maintenance",
                table: "maintenance_work_order_spare_part_lines");

            migrationBuilder.DropColumn(
                name: "site_code",
                schema: "maintenance",
                table: "maintenance_work_order_spare_part_lines");
        }
    }
}
