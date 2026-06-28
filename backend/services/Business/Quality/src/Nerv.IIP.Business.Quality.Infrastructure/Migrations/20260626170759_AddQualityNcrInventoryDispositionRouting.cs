using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityNcrInventoryDispositionRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "location_code",
                schema: "quality",
                table: "nonconformance_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional inventory location snapshot copied from the source inspection for NCR disposition stock movements.");

            migrationBuilder.AddColumn<string>(
                name: "owner_id",
                schema: "quality",
                table: "nonconformance_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional inventory owner id snapshot copied from the source inspection for NCR disposition stock movements.");

            migrationBuilder.AddColumn<string>(
                name: "owner_type",
                schema: "quality",
                table: "nonconformance_reports",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Optional inventory owner type snapshot copied from the source inspection for NCR disposition stock movements.");

            migrationBuilder.AddColumn<string>(
                name: "site_code",
                schema: "quality",
                table: "nonconformance_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional inventory site snapshot copied from the source inspection for NCR disposition stock movements.");

            migrationBuilder.AddColumn<string>(
                name: "uom_code",
                schema: "quality",
                table: "nonconformance_reports",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Optional inventory UOM snapshot copied from the source inspection for NCR disposition stock movements.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "location_code",
                schema: "quality",
                table: "nonconformance_reports");

            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "quality",
                table: "nonconformance_reports");

            migrationBuilder.DropColumn(
                name: "owner_type",
                schema: "quality",
                table: "nonconformance_reports");

            migrationBuilder.DropColumn(
                name: "site_code",
                schema: "quality",
                table: "nonconformance_reports");

            migrationBuilder.DropColumn(
                name: "uom_code",
                schema: "quality",
                table: "nonconformance_reports");
        }
    }
}
