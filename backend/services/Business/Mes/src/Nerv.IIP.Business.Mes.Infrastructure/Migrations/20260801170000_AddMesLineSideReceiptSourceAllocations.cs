using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesLineSideReceiptSourceAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "pending_issue_leg_count",
                schema: "mes",
                table: "material_issue_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Number of warehouse outbound posting details expected for the in-flight line-side receipt.");

            migrationBuilder.AddColumn<string>(
                name: "pending_issue_leg_posted_indexes_json",
                schema: "mes",
                table: "material_issue_requests",
                type: "text",
                nullable: false,
                defaultValue: "[]",
                comment: "JSON array of warehouse outbound detail indexes already acknowledged by Inventory.");

            migrationBuilder.AddColumn<string>(
                name: "source_allocations_json",
                schema: "mes",
                table: "material_issue_requests",
                type: "text",
                nullable: false,
                defaultValue: "[]",
                comment: "JSON array of actual source location, lot and quantity allocations used to emit multiple Inventory posting details.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pending_issue_leg_count",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "pending_issue_leg_posted_indexes_json",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "source_allocations_json",
                schema: "mes",
                table: "material_issue_requests");
        }
    }
}
