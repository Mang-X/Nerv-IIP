/**
 * WMS status code → 中文标签（纯 TS，UI 不暴露工程态码）。
 * 未知/空码统一回退「未知状态」；匹配大小写不敏感。
 */
const UNKNOWN = '未知状态'

function resolve(map: Record<string, string>, code: string | null | undefined): string {
  if (!code) return UNKNOWN
  return map[code.toLowerCase()] ?? UNKNOWN
}

const WAREHOUSE_TASK_STATUS: Record<string, string> = {
  open: '待执行',
  inprogress: '执行中',
  exception: '异常待处理',
  completed: '已完成',
  completedwithdifference: '差异完成',
  cancelled: '已取消',
}

const COUNT_EXECUTION_STATUS: Record<string, string> = {
  open: '待盘点',
  completed: '已完成',
}

const INBOUND_ORDER_STATUS: Record<string, string> = {
  open: '待收货',
  completed: '已完成',
  inventorypostingfailed: '库存过账失败',
  pendingqualitycheck: '待质检',
  cancelled: '已取消',
}

const OUTBOUND_ORDER_STATUS: Record<string, string> = {
  open: '待复核发货',
  completed: '已完成',
  inventorypostingfailed: '库存过账失败',
  cancelled: '已取消',
  inventorypostingpending: '库存过账中',
}

export function warehouseTaskStatusLabel(code: string | null | undefined): string {
  return resolve(WAREHOUSE_TASK_STATUS, code)
}

export function countExecutionStatusLabel(code: string | null | undefined): string {
  return resolve(COUNT_EXECUTION_STATUS, code)
}

export function inboundOrderStatusLabel(code: string | null | undefined): string {
  return resolve(INBOUND_ORDER_STATUS, code)
}

export function outboundOrderStatusLabel(code: string | null | undefined): string {
  return resolve(OUTBOUND_ORDER_STATUS, code)
}
