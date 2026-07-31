using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMovementRequestedUnitCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "unit_cost",
                schema: "inventory",
                table: "stock_movements",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Effective movement unit cost after moving-average valuation; outbound is always rewritten with the ledger moving-average cost, so this is a derived fact rather than caller payload.",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true,
                oldComment: "Optional movement unit cost used for moving-average valuation.");

            migrationBuilder.AddColumn<decimal>(
                name: "requested_unit_cost",
                schema: "inventory",
                table: "stock_movements",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Caller-supplied unit cost exactly as posted, never overwritten by valuation; null means the caller left costing to the ledger. Idempotency payload comparison uses this column instead of unit_cost.");

            // 既有行回填口径（两段，逐段可证）：
            // 1) quantity > 0（入库腿）：unit_cost 就是调用方原值，逐字精确，直接复制。
            //    StockLedger.ApplyValuation 对入库调用 movement.ApplyValuation(movement.UnitCost ?? MovingAverageUnitCost)，
            //    而 StockMovement.ApplyValuation 只在 `UnitCost is not null || Quantity < 0` 时写回——
            //    入库带成本时写回的正是同一个值，入库不带成本时保持 NULL。故入库行 unit_cost ≡ 调用方原值。
            // 2) quantity < 0（出库腿）：调用方原值不可恢复，保持 NULL。
            //    出库一律被移动平均成本覆写，落库值与调用方载荷无关。若回填成 unit_cost，所有历史出库行在
            //    「重放同样不传成本」时会继续误报 IDEMPOTENCY_CONFLICT——#1332 的缺陷在存量数据上等于没修；
            //    若引入「迁移前未知 ⇒ 宽松比较」，则任意成本的重放都被静默接受，正是本次要堵的静默错账。
            //    保持 NULL 让历史出库行与修复后新写入的出库行行为一致：不传成本重放=幂等成功、传了成本重放=冲突。
            //    已知残留：迁移前「显式传过成本的调拨」，其重放若改成不传成本会被判为幂等成功而非冲突。
            //    该方向是调用方给出更少信息、且首次过账已定型不会被重新计价，属可接受的最小残留。
            migrationBuilder.Sql("""
                UPDATE inventory.stock_movements
                SET requested_unit_cost = unit_cost
                WHERE quantity > 0 AND unit_cost IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "requested_unit_cost",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_cost",
                schema: "inventory",
                table: "stock_movements",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Optional movement unit cost used for moving-average valuation.",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true,
                oldComment: "Effective movement unit cost after moving-average valuation; outbound is always rewritten with the ledger moving-average cost, so this is a derived fact rather than caller payload.");
        }
    }
}
