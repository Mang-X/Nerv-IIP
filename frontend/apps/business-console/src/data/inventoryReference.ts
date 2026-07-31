/**
 * 库存域 · 前端受控值常量。
 *
 * 库存移动类型在 Gateway 契约里只是自由 `string`——类型层拦不住漂移，只有把码值集中到
 * 这里并由 `inventoryReference.test.ts` 锁住，才不会再和后端各写各的。两个层次要分清：
 *
 * 1. **台账里真实存在的类型**（读面）= 后端聚合 `StockMovement.SupportedMovementTypes`；
 * 2. **外部命令接受的类型**（写面）= `PostStockMovementCommandHandler.ExternalMovementTypes`，
 *    是第 1 层的子集——盘点调整与状态转出入只能由盘点 / 状态转移命令自己产生，
 *    人工补录面（新建移动弹框）提供的就是这一层，不多不少。
 *
 * 走查台账 #49：弹框此前给的 `receipt` / `issue` 两个码值后端从不接受（默认值还正是
 * `receipt`），照界面提供的类型走必 400 `Movement type 'receipt' cannot be posted through the
 * external stock movement command.`——UI 提供了必失败的选项。这两个幽灵值就此清掉。
 *
 * 走查台账 #68（`transfer` 单腿凭空增减库存）已由 #1359 从**后端**治本：调拨改为一次提交
 * 配平两腿（出库腿负、入库腿等额为正，合计为零），缺腿 / 不配平 / 同库位一律整笔拒绝。
 * 对应地，新建移动弹框选「移库」时会多出「入库库位」必填项、按调拨量正数录入、提交时
 * 自动拆成 -N / +N 两腿。所以 `transfer` 是**合法且必要**的选项，人工补录面照常提供。
 */
import type { RefOption } from './masterDataReference'

/**
 * 库存流水读面的类型标签，与后端 `StockMovement.SupportedMovementTypes` 逐字一致。
 * 台账里能出现的就是这 7 种，不多不少。
 */
export const INVENTORY_MOVEMENT_TYPE_LABELS: Readonly<Record<string, string>> = {
  inbound: '入库',
  outbound: '出库',
  transfer: '移库',
  adjustment: '调整',
  'count-adjustment': '盘点调整',
  'status-transfer-out': '状态转出',
  'status-transfer-in': '状态转入',
}

/**
 * 外部库存移动命令接受的类型，与后端 `PostStockMovementCommandHandler.ExternalMovementTypes`
 * 逐字一致。除此之外的码值经 `POST /inventory/movements` 一律被拒。
 */
export const INVENTORY_EXTERNAL_MOVEMENT_TYPES = ['inbound', 'outbound', 'transfer', 'adjustment']

/**
 * 人工补录面（新建移动弹框）提供的类型：与 `INVENTORY_EXTERNAL_MOVEMENT_TYPES` **一一对应**。
 * 界面能选的，后端就一定收——多一个是必失败的幽灵值，少一个是白白砍掉的能力。
 */
export const INVENTORY_MANUAL_MOVEMENT_TYPE_OPTIONS: RefOption[] = [
  { value: 'inbound', label: '入库' },
  { value: 'outbound', label: '出库' },
  { value: 'transfer', label: '移库' },
  { value: 'adjustment', label: '调整' },
]

/** 弹框默认类型：必须是人工补录面真的能过账的类型。 */
export const INVENTORY_MANUAL_MOVEMENT_DEFAULT_TYPE =
  INVENTORY_MANUAL_MOVEMENT_TYPE_OPTIONS[0].value

/** 移动类型码值 → 中文标签；未知码值如实回显，不编名字。 */
export function inventoryMovementTypeLabel(movementType?: string | null, fallback = '—') {
  if (!movementType) return fallback
  return INVENTORY_MOVEMENT_TYPE_LABELS[movementType] ?? movementType
}
