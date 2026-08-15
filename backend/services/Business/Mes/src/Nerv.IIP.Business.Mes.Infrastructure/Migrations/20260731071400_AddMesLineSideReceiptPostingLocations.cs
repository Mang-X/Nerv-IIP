using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesLineSideReceiptPostingLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "pending_issue_leg_posted",
                schema: "mes",
                table: "material_issue_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether the warehouse outbound leg of the in-flight line-side receipt has been posted by Inventory.");

            migrationBuilder.AddColumn<string>(
                name: "pending_posting_token",
                schema: "mes",
                table: "material_issue_requests",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                comment: "Normalized cross-leg idempotency token of the in-flight line-side receipt posting; null when nothing is in flight.");

            migrationBuilder.AddColumn<bool>(
                name: "pending_receipt_leg_posted",
                schema: "mes",
                table: "material_issue_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether the line-side inbound leg of the in-flight line-side receipt has been posted by Inventory.");

            migrationBuilder.AddColumn<decimal>(
                name: "pending_receipt_quantity",
                schema: "mes",
                table: "material_issue_requests",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Line-side receipt quantity submitted to Inventory but not yet posted on both transfer legs; kitting never counts it.");

            migrationBuilder.AddColumn<int>(
                name: "receipt_attempt",
                schema: "mes",
                table: "material_issue_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Monotonic line-side receipt attempt number stamped into the Inventory idempotency key so a failed attempt never blocks the retry.");

            migrationBuilder.AddColumn<string>(
                name: "source_location_code",
                schema: "mes",
                table: "material_issue_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Inventory location code the material is actually issued from, resolved from real stock holdings.");

            migrationBuilder.AddColumn<string>(
                name: "source_site_code",
                schema: "mes",
                table: "material_issue_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Inventory site code the material is actually issued from, resolved from real stock holdings instead of a hardcoded namespace.");

            migrationBuilder.AddColumn<string>(
                name: "target_location_code",
                schema: "mes",
                table: "material_issue_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Inventory location code of the work station line-side destination.");

            migrationBuilder.AddColumn<string>(
                name: "target_site_code",
                schema: "mes",
                table: "material_issue_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Inventory site code of the work station line-side destination.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pending_issue_leg_posted",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "pending_posting_token",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "pending_receipt_leg_posted",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "pending_receipt_quantity",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "receipt_attempt",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "source_location_code",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "source_site_code",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "target_location_code",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "target_site_code",
                schema: "mes",
                table: "material_issue_requests");
        }
    }
}
