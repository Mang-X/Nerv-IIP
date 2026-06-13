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
  pending: '待执行',
  inprogress: '执行中',
  completed: '已完成',
  cancelled: '已取消',
  canceled: '已取消',
}

const COUNT_EXECUTION_STATUS: Record<string, string> = {
  pending: '待盘点',
  inprogress: '盘点中',
  completed: '已完成',
  cancelled: '已取消',
  canceled: '已取消',
}

const INBOUND_ORDER_STATUS: Record<string, string> = {
  open: '待入库',
  pending: '待入库',
  inprogress: '入库中',
  completed: '已入库',
  closed: '已关闭',
  cancelled: '已取消',
  canceled: '已取消',
}

const OUTBOUND_ORDER_STATUS: Record<string, string> = {
  open: '待发货',
  pending: '待发货',
  inprogress: '发货中',
  completed: '已发货',
  closed: '已关闭',
  cancelled: '已取消',
  canceled: '已取消',
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
