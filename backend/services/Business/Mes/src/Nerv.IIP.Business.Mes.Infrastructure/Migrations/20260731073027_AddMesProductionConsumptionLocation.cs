using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesProductionConsumptionLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "location_code",
                schema: "mes",
                table: "production_report_material_consumptions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Inventory location code the material was consumed from, copied from the supplying material issue request line-side target.");

            migrationBuilder.AddColumn<string>(
                name: "site_code",
                schema: "mes",
                table: "production_report_material_consumptions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Inventory site code the material was consumed from, copied from the supplying material issue request line-side target.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "location_code",
                schema: "mes",
                table: "production_report_material_consumptions");

            migrationBuilder.DropColumn(
                name: "site_code",
                schema: "mes",
                table: "production_report_material_consumptions");
        }
    }
}
