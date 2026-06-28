using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesInventoryPostingFailureConsumptionFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "inventory_posting_failed_at_utc",
                schema: "mes",
                table: "production_report_material_consumptions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when Inventory rejected the latest MES production material consumption posting.");

            migrationBuilder.AddColumn<string>(
                name: "inventory_posting_failure_code",
                schema: "mes",
                table: "production_report_material_consumptions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Last Inventory posting failure code returned for this MES production material consumption.");

            migrationBuilder.AddColumn<string>(
                name: "inventory_posting_failure_message",
                schema: "mes",
                table: "production_report_material_consumptions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Last Inventory posting failure message returned for this MES production material consumption.");

            migrationBuilder.AddColumn<string>(
                name: "inventory_posting_rollback_key",
                schema: "mes",
                table: "material_issue_requests",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                comment: "MES normalized receipt-step key already rolled back for Inventory posting failure, used to avoid double rollback when both transfer legs fail.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "inventory_posting_failed_at_utc",
                schema: "mes",
                table: "production_report_material_consumptions");

            migrationBuilder.DropColumn(
                name: "inventory_posting_failure_code",
                schema: "mes",
                table: "production_report_material_consumptions");

            migrationBuilder.DropColumn(
                name: "inventory_posting_failure_message",
                schema: "mes",
                table: "production_report_material_consumptions");

            migrationBuilder.DropColumn(
                name: "inventory_posting_rollback_key",
                schema: "mes",
                table: "material_issue_requests");
        }
    }
}
