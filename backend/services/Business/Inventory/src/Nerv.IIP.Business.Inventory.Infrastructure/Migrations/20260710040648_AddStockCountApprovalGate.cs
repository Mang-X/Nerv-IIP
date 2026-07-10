using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockCountApprovalGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "stock_count_tasks",
                schema: "inventory",
                comment: "Inventory stock count tasks, expected ledger version snapshots, approval state and confirmed variances.",
                oldComment: "Inventory stock count tasks, expected ledger version snapshots and confirmed variances.");

            migrationBuilder.AlterTable(
                name: "stock_count_adjustments",
                schema: "inventory",
                comment: "Inventory stock count adjustment facts, including pending approval, posted and voided variances.",
                oldComment: "Inventory stock count adjustment facts generated from confirmed count variances.");

            migrationBuilder.AlterColumn<string>(
                name: "movement_id",
                schema: "inventory",
                table: "stock_count_adjustments",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Stock movement id generated only after the count variance is posted.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldComment: "Stock movement id generated for the count variance.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "confirmed_at_utc",
                schema: "inventory",
                table: "stock_count_adjustments",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the count adjustment was posted after approval or auto-routing.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "UTC time when the count adjustment was confirmed.");

            migrationBuilder.AddColumn<string>(
                name: "approval_chain_id",
                schema: "inventory",
                table: "stock_count_adjustments",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "BusinessApproval chain id when the variance exceeds an approval threshold.");

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "inventory",
                table: "stock_count_adjustments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "posted",
                comment: "Count adjustment lifecycle status: pending-approval, posted or voided.");

            migrationBuilder.AddColumn<decimal>(
                name: "variance_amount",
                schema: "inventory",
                table: "stock_count_adjustments",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Absolute variance value at the ledger moving-average unit cost used for approval routing.");

            migrationBuilder.CreateIndex(
                name: "ux_stock_count_adjustments_approval_chain",
                schema: "inventory",
                table: "stock_count_adjustments",
                columns: new[] { "organization_id", "environment_id", "approval_chain_id" },
                unique: true,
                filter: "approval_chain_id is not null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_count_adjustments_status",
                schema: "inventory",
                table: "stock_count_adjustments",
                sql: "status in ('pending-approval','posted','voided')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_stock_count_adjustments_approval_chain",
                schema: "inventory",
                table: "stock_count_adjustments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_count_adjustments_status",
                schema: "inventory",
                table: "stock_count_adjustments");

            migrationBuilder.DropColumn(
                name: "approval_chain_id",
                schema: "inventory",
                table: "stock_count_adjustments");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "inventory",
                table: "stock_count_adjustments");

            migrationBuilder.DropColumn(
                name: "variance_amount",
                schema: "inventory",
                table: "stock_count_adjustments");

            migrationBuilder.AlterTable(
                name: "stock_count_tasks",
                schema: "inventory",
                comment: "Inventory stock count tasks, expected ledger version snapshots and confirmed variances.",
                oldComment: "Inventory stock count tasks, expected ledger version snapshots, approval state and confirmed variances.");

            migrationBuilder.AlterTable(
                name: "stock_count_adjustments",
                schema: "inventory",
                comment: "Inventory stock count adjustment facts generated from confirmed count variances.",
                oldComment: "Inventory stock count adjustment facts, including pending approval, posted and voided variances.");

            migrationBuilder.AlterColumn<string>(
                name: "movement_id",
                schema: "inventory",
                table: "stock_count_adjustments",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "",
                comment: "Stock movement id generated for the count variance.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true,
                oldComment: "Stock movement id generated only after the count variance is posted.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "confirmed_at_utc",
                schema: "inventory",
                table: "stock_count_adjustments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                comment: "UTC time when the count adjustment was confirmed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "UTC time when the count adjustment was posted after approval or auto-routing.");
        }
    }
}
