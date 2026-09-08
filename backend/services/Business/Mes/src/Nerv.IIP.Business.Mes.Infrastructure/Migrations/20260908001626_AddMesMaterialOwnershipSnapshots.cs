using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesMaterialOwnershipSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "owner_id",
                schema: "mes",
                table: "production_report_material_consumptions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Frozen inventory owner id; null for company and legacy production ownership.");

            migrationBuilder.AddColumn<string>(
                name: "owner_type",
                schema: "mes",
                table: "production_report_material_consumptions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "production",
                comment: "Frozen inventory owner type; historical consumption facts retain production ownership.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "mes",
                table: "production_report_material_consumptions");

            migrationBuilder.DropColumn(
                name: "owner_type",
                schema: "mes",
                table: "production_report_material_consumptions");
        }
    }
}
