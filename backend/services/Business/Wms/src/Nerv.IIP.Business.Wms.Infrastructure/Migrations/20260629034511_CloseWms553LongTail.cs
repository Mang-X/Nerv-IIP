using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CloseWms553LongTail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "backorder_quantity",
                schema: "wms",
                table: "outbound_order_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Short-picked outbound quantity left as backorder.");

            migrationBuilder.AddColumn<decimal>(
                name: "issued_quantity",
                schema: "wms",
                table: "outbound_order_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Actual outbound quantity issued after picking and pack review.");

            migrationBuilder.AddColumn<string>(
                name: "inventory_count_task_id",
                schema: "wms",
                table: "count_executions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Public Inventory count task id used to freeze and confirm the counted ledger.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "backorder_quantity",
                schema: "wms",
                table: "outbound_order_lines");

            migrationBuilder.DropColumn(
                name: "issued_quantity",
                schema: "wms",
                table: "outbound_order_lines");

            migrationBuilder.DropColumn(
                name: "inventory_count_task_id",
                schema: "wms",
                table: "count_executions");
        }
    }
}
