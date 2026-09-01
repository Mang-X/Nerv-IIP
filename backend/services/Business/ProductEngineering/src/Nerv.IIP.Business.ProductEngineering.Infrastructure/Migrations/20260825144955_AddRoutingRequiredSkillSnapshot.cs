using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutingRequiredSkillSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "required_skill_code",
                schema: "product_engineering",
                table: "routing_operations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional MasterData skill code required to perform the routing operation, captured at routing release.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "required_skill_code",
                schema: "product_engineering",
                table: "routing_operations");
        }
    }
}
