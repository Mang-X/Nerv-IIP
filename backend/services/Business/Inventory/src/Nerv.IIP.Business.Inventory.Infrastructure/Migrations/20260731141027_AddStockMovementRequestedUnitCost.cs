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
                comment: "Unit cost as supplied to the posting call, never overwritten by valuation; null means costing was left to the ledger. Idempotency payload comparison uses this column instead of unit_cost. Exception: the synthesized transfer inbound leg and the status-transfer-in leg are constructed with the source ledger moving-average cost, so those rows carry a derived value here; their payload is not compared today, and any future inbound-leg comparison must first make those two call sites pass the real caller value.");

            // 既有行回填口径（三段，逐段可证）：
            //
            // 1) 只回填 movement_type IN ('inbound','adjustment') 且 quantity > 0 且 unit_cost IS NOT NULL 的行。
            //    这批行的 unit_cost 逐字等于调用方原值：唯一构造入口 PostStockMovementCommand.CreateMovementOrReject
            //    直接传 request.UnitCost；随后 StockLedger.ApplyValuation 对入库调用
            //    movement.ApplyValuation(movement.UnitCost ?? MovingAverageUnitCost)，而 StockMovement.ApplyValuation
            //    只在 `UnitCost is not null || Quantity < 0` 时写回——带成本时写回的正是同一个值（恒等），
            //    不带成本时保持 NULL（已被 unit_cost IS NOT NULL 排除）。故这批行可精确还原。
            //
            // 2) 另外两类 quantity > 0 的「合成入库腿」必须排除：它们在构造时就被喂了派生值，不是调用方原值。
            //    - movement_type = 'transfer' 的入库腿：PostStockMovementCommand.CreateTransferInMovementOrReject
            //      传 `request.UnitCost ?? sourceMovingAverageUnitCost`，调用方传 null 时落库的是源台账移动平均。
            //    - movement_type = 'status-transfer-in'：PostStockStatusTransferCommand 传 source.MovingAverageUnitCost，
            //      该命令根本没有 UnitCost 入参，恒为派生值。
            //    把它们的 unit_cost 复制进来会让本列名不副实（列语义是「调用方原值」）：后续若有人给入库腿
            //    补载荷比较，就会拿会漂移的移动平均去比对，制造随时间随机出现的假冲突。留 NULL 只会让这批行
            //    在重放时偏向报冲突（吵闹但安全），方向正确。
            //    count-adjustment 的正向调整不传 unitCost、unit_cost 恒为 NULL，已被 NULL 条件自然排除。
            //
            // 3) quantity < 0（各类出库腿）：调用方原值不可恢复，保持 NULL。
            //    出库一律被移动平均成本覆写，落库值与调用方载荷无关。若回填成 unit_cost，所有历史出库行在
            //    「重放同样不传成本」时会继续误报 IDEMPOTENCY_CONFLICT——#1332 的缺陷在存量数据上等于没修；
            //    若引入「迁移前未知 ⇒ 宽松比较」，则任意成本的重放都被静默接受，正是本次要堵的静默错账。
            //    保持 NULL 让历史出库行与修复后新写入的出库行行为一致。
            //
            // 存量行的两个方向残留（原值已不可恢复，残留本身不可避免，此处只作披露）：
            //    - 迁移前显式传过成本的调拨，重放若改成「不传成本」→ NULL vs NULL 判幂等成功，而非冲突。
            //    - 迁移前显式传过成本的出库 / 调拨出库腿，重放若传「同一个成本」→ NULL vs 99 误报冲突，
            //      即 #1332 的原症状在这批存量行上仍然存在。
            //    两个方向都只影响迁移前写入的行；迁移后新写入的行两种场景均判定正确。
            //
            // 运维提示：这是 stock_movements 的全表 UPDATE，而 EF 迁移默认在单事务内执行，
            // 大数据量实例会产生较长事务与行版本膨胀。未改为分批——分批仍在同一事务内，
            // 既不缩短锁持有时间也不减少膨胀，只增加语句数；真正的缓解手段（维护窗口、事后 VACUUM）
            // 在迁移之外。建议大表实例在维护窗口执行本迁移，并在其后对该表 VACUUM。
            migrationBuilder.Sql("""
                UPDATE inventory.stock_movements
                SET requested_unit_cost = unit_cost
                WHERE quantity > 0
                  AND unit_cost IS NOT NULL
                  AND movement_type IN ('inbound', 'adjustment');
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
