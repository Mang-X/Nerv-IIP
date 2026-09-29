using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutingInterruptible : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "interruptible",
                schema: "product_engineering",
                table: "routing_operations",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether this routing operation may be interrupted across scheduling windows.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "interruptible",
                schema: "product_engineering",
                table: "routing_operations");
        }
    }
}
