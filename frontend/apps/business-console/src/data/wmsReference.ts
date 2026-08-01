/**
 * WMS 受控值：状态与单据类型。
 *
 * 这些值域是后端枚举而不是主数据目录，没有、也不该有列表读面，所以在前端集中成常量
 * （AGENTS §6：业务取值走字典 / 常量模块，禁止散落在页面里写死）。
 * 码值与后端保持一致：
 * - `WarehouseTaskStatus` Open / InProgress / Completed / CompletedWithDifference / Exception / Cancelled
 * - `CountExecutionStatus` Open / Completed
 * - `InboundOrderStatus` Open / Completed / InventoryPostingFailed / PendingQualityCheck / Cancelled
 * - `OutboundOrderStatus` Open / Completed / InventoryPostingFailed / Cancelled / InventoryPostingPending
 */
import type { SearchSelectOption } from '@nerv-iip/ui'

/** 「全部」在筛选条里用这个哨兵值，避免和真实码值撞。 */
export const WMS_STATUS_ANY = 'all'

function withAnyOption(options: SearchSelectOption[], anyLabel: string): SearchSelectOption[] {
  return [{ value: WMS_STATUS_ANY, label: anyLabel }, ...options]
}

/** 仓库作业任务（上架 / 拣货 / 补货）状态。 */
export const WMS_WAREHOUSE_TASK_STATUS_OPTIONS: SearchSelectOption[] = [
  { value: 'Open', label: '待执行' },
  { value: 'InProgress', label: '执行中' },
  { value: 'Completed', label: '已完成' },
  { value: 'CompletedWithDifference', label: '差异完成' },
  { value: 'Exception', label: '异常待处理' },
  { value: 'Cancelled', label: '已取消' },
]

export const WMS_COUNT_EXECUTION_STATUS_OPTIONS: SearchSelectOption[] = [
  { value: 'Open', label: '待盘点' },
  { value: 'Completed', label: '已完成' },
]

export const WMS_INBOUND_ORDER_STATUS_OPTIONS: SearchSelectOption[] = [
  { value: 'Open', label: '待收货' },
  { value: 'PendingQualityCheck', label: '待质检' },
  { value: 'Completed', label: '已完成' },
  { value: 'InventoryPostingFailed', label: '库存过账失败' },
  { value: 'Cancelled', label: '已取消' },
]

export const WMS_OUTBOUND_ORDER_STATUS_OPTIONS: SearchSelectOption[] = [
  { value: 'Open', label: '待出库' },
  { value: 'InventoryPostingPending', label: '库存过账中' },
  { value: 'Completed', label: '已完成' },
  { value: 'InventoryPostingFailed', label: '库存过账失败' },
  { value: 'Cancelled', label: '已取消' },
]

/** 上下游单据类型：入库来自采购收货或生产完工，出库去向生产领料或销售发货。 */
export const WMS_INBOUND_SOURCE_TYPE_OPTIONS: SearchSelectOption[] = [
  { value: 'PurchaseReceipt', label: '采购收货' },
  { value: 'ProductionReceipt', label: '生产完工入库' },
  { value: 'SalesReturn', label: '销售退货' },
  { value: 'InventoryTransfer', label: '库存调拨' },
]

export const WMS_OUTBOUND_SOURCE_TYPE_OPTIONS: SearchSelectOption[] = [
  { value: 'ProductionIssue', label: '生产领料' },
  { value: 'SalesDelivery', label: '销售发货' },
  { value: 'PurchaseReturn', label: '采购退货' },
  { value: 'InventoryTransfer', label: '库存调拨' },
]

export const wmsWarehouseTaskStatusFilterOptions = withAnyOption(
  WMS_WAREHOUSE_TASK_STATUS_OPTIONS,
  '全部状态',
)
export const wmsCountExecutionStatusFilterOptions = withAnyOption(
  WMS_COUNT_EXECUTION_STATUS_OPTIONS,
  '全部状态',
)
export const wmsInboundOrderStatusFilterOptions = withAnyOption(
  WMS_INBOUND_ORDER_STATUS_OPTIONS,
  '全部状态',
)
export const wmsOutboundOrderStatusFilterOptions = withAnyOption(
  WMS_OUTBOUND_ORDER_STATUS_OPTIONS,
  '全部状态',
)

function labelOf(options: SearchSelectOption[], value?: string | null) {
  const trimmed = value?.trim()
  if (!trimmed) return '—'
  // 后端若出现常量表还没覆盖的新码值，原样回落显示，不吞数据。
  return options.find((option) => option.value === trimmed)?.label ?? trimmed
}

export const wmsWarehouseTaskStatusLabel = (value?: string | null) =>
  labelOf(WMS_WAREHOUSE_TASK_STATUS_OPTIONS, value)
export const wmsCountExecutionStatusLabel = (value?: string | null) =>
  labelOf(WMS_COUNT_EXECUTION_STATUS_OPTIONS, value)
export const wmsInboundOrderStatusLabel = (value?: string | null) =>
  labelOf(WMS_INBOUND_ORDER_STATUS_OPTIONS, value)
export const wmsOutboundOrderStatusLabel = (value?: string | null) =>
  labelOf(WMS_OUTBOUND_ORDER_STATUS_OPTIONS, value)

/**
 * 仓库任务「为什么不能操作」的中文说明（#1397 / 台账 #82）。
 *
 * 后端每行任务都回 `allowedActions` + `blockReasons`；PC 端过去两者都没用，于是
 * 「两张待执行任务都没有任何按钮」，页面也不说为什么。这里把代码翻成人话，
 * 让「派给别人了」和「已经结束了」在界面上长得不一样。
 *
 * 文案与 PDA 的 `WarehouseTaskExecutionView.vue` 保持一致——同一个代码在两个端
 * 说法不同，现场对不上话。
 */
const WAREHOUSE_TASK_BLOCK_REASON_LABELS: Record<string, string> = {
  TASK_TERMINAL: '任务已结束，不可继续操作',
  TASK_TYPE_NOT_MANUALLY_EXECUTABLE: '该任务类型不支持人工执行',
  ACTOR_CONTEXT_MISSING: '取不到当前操作人身份，请重新登录后重试',
  TASK_NOT_ASSIGNED_TO_WORK_POOL: '任务尚未分配作业池，请先由当班负责人分配',
  TASK_ASSIGNED_TO_ANOTHER_OPERATOR: '任务已派给其他人员，请联系当班负责人改派',
  TASK_EXECUTION_CLAIMED_BY_WCS: '任务已由 WCS 接管，请在自动化设备侧处理',
  TASK_EXECUTION_CLAIMED_BY_ANOTHER_OPERATOR: '任务正由其他人员执行',
  TASK_EXECUTION_NOT_CLAIMED: '任务尚未开始执行',
}

/** 单条阻断原因的中文；未登记的代码原样回落，不吞数据也不编话。 */
export const warehouseTaskBlockReasonLabel = (reason?: string | null) => {
  const trimmed = reason?.trim()
  if (!trimmed) return ''
  return WAREHOUSE_TASK_BLOCK_REASON_LABELS[trimmed] ?? `当前任务不可操作（${trimmed}）`
}

/** 多条阻断原因合成一句；无原因时返回空串，由调用方决定要不要显示。 */
export const warehouseTaskBlockReasonText = (reasons?: readonly string[] | null) =>
  (reasons ?? [])
    .map((reason) => warehouseTaskBlockReasonLabel(reason))
    .filter(Boolean)
    .join('；')
