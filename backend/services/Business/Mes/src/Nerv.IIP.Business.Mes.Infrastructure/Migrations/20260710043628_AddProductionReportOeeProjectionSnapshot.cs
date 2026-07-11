using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionReportOeeProjectionSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "oee_device_asset_id",
                schema: "mes",
                table: "production_reports",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Assigned device snapshot carried with the report for OEE projection and reversal consistency.");

            migrationBuilder.AddColumn<decimal>(
                name: "oee_theoretical_rate_per_hour",
                schema: "mes",
                table: "production_reports",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Theoretical output-rate snapshot carried with the report for OEE projection and reversal consistency.");

            migrationBuilder.AddColumn<string>(
                name: "oee_uom_code",
                schema: "mes",
                table: "production_reports",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                comment: "Output unit snapshot carried with the report for OEE projection and reversal consistency.");

            migrationBuilder.AddColumn<string>(
                name: "oee_work_center_id",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Work center snapshot carried with the report for OEE projection and reversal consistency.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "oee_device_asset_id",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "oee_theoretical_rate_per_hour",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "oee_uom_code",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "oee_work_center_id",
                schema: "mes",
                table: "production_reports");
        }
    }
}
