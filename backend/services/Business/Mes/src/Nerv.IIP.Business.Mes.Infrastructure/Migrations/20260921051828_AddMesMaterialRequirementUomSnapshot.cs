using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesMaterialRequirementUomSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "uom_code",
                schema: "mes",
                table: "material_requirements",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "UNSPECIFIED",
                comment: "Unit of measure frozen with the released MBOM material requirement.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "uom_code",
                schema: "mes",
                table: "material_requirements");
        }
    }
}
