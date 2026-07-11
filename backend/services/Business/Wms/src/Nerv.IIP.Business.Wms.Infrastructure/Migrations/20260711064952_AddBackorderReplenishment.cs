using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBackorderReplenishment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "warehouse_tasks",
                schema: "wms",
                comment: "WMS putaway, picking and replenishment recommendation tasks.",
                oldComment: "WMS putaway and picking warehouse tasks.");

            migrationBuilder.AlterColumn<string>(
                name: "task_type",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Task type: putaway, picking or replenishment.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Task type: putaway or picking.");

            migrationBuilder.CreateTable(
                name: "backorder_orders",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Backorder order aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    backorder_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Stable WMS backorder order number."),
                    outbound_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Short-picked WMS outbound order number."),
                    outbound_order_line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Short-picked WMS outbound order line number."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Short-picked SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Short-picked unit of measure."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Site where the short pick occurred."),
                    pick_location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Pick face targeted by the replenishment recommendation."),
                    backorder_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Unfulfilled quantity recorded by pack review."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Backorder lifecycle status."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the short pick created the backorder."),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the backorder was closed."),
                    closure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "Audited reason for closing the backorder.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backorder_orders", x => x.id);
                },
                comment: "Durable WMS short-pick backorder facts that drive replenishment recommendations.");

            migrationBuilder.CreateIndex(
                name: "IX_backorder_orders_organization_id_environment_id_backorder_o~",
                schema: "wms",
                table: "backorder_orders",
                columns: new[] { "organization_id", "environment_id", "backorder_order_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_backorder_orders_organization_id_environment_id_outbound_or~",
                schema: "wms",
                table: "backorder_orders",
                columns: new[] { "organization_id", "environment_id", "outbound_order_no", "outbound_order_line_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backorder_orders",
                schema: "wms");

            migrationBuilder.AlterTable(
                name: "warehouse_tasks",
                schema: "wms",
                comment: "WMS putaway and picking warehouse tasks.",
                oldComment: "WMS putaway, picking and replenishment recommendation tasks.");

            migrationBuilder.AlterColumn<string>(
                name: "task_type",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Task type: putaway or picking.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Task type: putaway, picking or replenishment.");
        }
    }
}
