using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesInventoryPostingFailureFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "inventory_posting_failed_at_utc",
                schema: "mes",
                table: "material_issue_requests",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when Inventory rejected the latest MES material issue or line-side receipt posting.");

            migrationBuilder.AddColumn<string>(
                name: "inventory_posting_failure_code",
                schema: "mes",
                table: "material_issue_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Last Inventory posting failure code returned for this MES material issue request.");

            migrationBuilder.AddColumn<string>(
                name: "inventory_posting_failure_message",
                schema: "mes",
                table: "material_issue_requests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Last Inventory posting failure message returned for this MES material issue request.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "inventory_posting_failed_at_utc",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when Inventory rejected the latest MES finished-goods receipt posting.");

            migrationBuilder.AddColumn<string>(
                name: "inventory_posting_failure_code",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Last Inventory posting failure code returned for this MES finished-goods receipt request.");

            migrationBuilder.AddColumn<string>(
                name: "inventory_posting_failure_message",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Last Inventory posting failure message returned for this MES finished-goods receipt request.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "inventory_posting_failed_at_utc",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "inventory_posting_failure_code",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "inventory_posting_failure_message",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "inventory_posting_failed_at_utc",
                schema: "mes",
                table: "finished_goods_receipt_requests");

            migrationBuilder.DropColumn(
                name: "inventory_posting_failure_code",
                schema: "mes",
                table: "finished_goods_receipt_requests");

            migrationBuilder.DropColumn(
                name: "inventory_posting_failure_message",
                schema: "mes",
                table: "finished_goods_receipt_requests");
        }
    }
}
