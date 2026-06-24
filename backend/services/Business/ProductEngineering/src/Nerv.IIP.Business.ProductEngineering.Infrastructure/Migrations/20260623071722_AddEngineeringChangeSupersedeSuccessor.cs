using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEngineeringChangeSupersedeSuccessor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "superseded_by_version_id",
                schema: "product_engineering",
                table: "engineering_change_affected_versions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional successor version id that supersedes this affected version.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "superseded_by_version_id",
                schema: "product_engineering",
                table: "engineering_change_affected_versions");
        }
    }
}
