using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockCountTaskIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                schema: "inventory",
                table: "stock_count_tasks",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Caller-provided stable idempotency key used to recover Inventory count freezes after RPC timeout.");

            migrationBuilder.Sql("""
                update inventory.stock_count_tasks
                set idempotency_key = 'count-code:' || count_task_code
                where idempotency_key is null;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key",
                schema: "inventory",
                table: "stock_count_tasks",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true,
                comment: "Caller-provided stable idempotency key used to recover Inventory count freezes after RPC timeout.",
                oldComment: "Caller-provided stable idempotency key used to recover Inventory count freezes after RPC timeout.");

            migrationBuilder.CreateIndex(
                name: "ux_stock_count_tasks_idempotency_key",
                schema: "inventory",
                table: "stock_count_tasks",
                columns: new[] { "organization_id", "environment_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_stock_count_tasks_idempotency_key",
                schema: "inventory",
                table: "stock_count_tasks");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "inventory",
                table: "stock_count_tasks");
        }
    }
}
