using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSkuShelfLifeThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "near_expiry_threshold_days",
                schema: "business_masterdata",
                table: "skus",
                type: "integer",
                nullable: true,
                comment: "Optional SKU near-expiry threshold in calendar days used by Inventory expiry alerts.");

            migrationBuilder.AddColumn<int>(
                name: "shelf_life_days",
                schema: "business_masterdata",
                table: "skus",
                type: "integer",
                nullable: true,
                comment: "Optional SKU default shelf life in calendar days used to derive batch expiry dates.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "near_expiry_threshold_days",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "shelf_life_days",
                schema: "business_masterdata",
                table: "skus");
        }
    }
}
