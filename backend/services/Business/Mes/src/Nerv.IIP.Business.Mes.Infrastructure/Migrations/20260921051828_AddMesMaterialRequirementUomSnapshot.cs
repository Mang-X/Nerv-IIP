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
                nullable: true,
                comment: "Unit of measure frozen with the released MBOM material requirement.");

            migrationBuilder.Sql("""
                UPDATE mes.material_requirements
                SET uom_code = 'UNSPECIFIED'
                WHERE uom_code IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "uom_code",
                schema: "mes",
                table: "material_requirements",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Unit of measure frozen with the released MBOM material requirement.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldComment: "Unit of measure frozen with the released MBOM material requirement.");
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
