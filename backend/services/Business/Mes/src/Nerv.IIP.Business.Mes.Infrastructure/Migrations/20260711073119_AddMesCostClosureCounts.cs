using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesCostClosureCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cost_report_count",
                schema: "mes",
                table: "work_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Count of MES reports expected by downstream actual-cost closure.");

            migrationBuilder.AddColumn<int>(
                name: "material_movement_count",
                schema: "mes",
                table: "work_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Count of Inventory material postings expected by downstream actual-cost closure.");

            migrationBuilder.AddColumn<int>(
                name: "material_movement_count",
                schema: "mes",
                table: "production_reports",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Count of production-consumption Inventory movements emitted for cost closure.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cost_report_count",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "material_movement_count",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "material_movement_count",
                schema: "mes",
                table: "production_reports");
        }
    }
}
