/**
 * MES 一线作业的可读中文状态标签（框架无关，纯 TS）。
 *
 * 这些映射原先在 4 个 PDA MES 页面（工序执行 / 报工 / 领料 / 完工入库）各自重复维护。
 * 集中到 business-core 后，页面只消费函数，不再复制标签表；渲染文案对每个已处理状态保持不变。
 *
 * 约定：未知/缺失状态一律回落到 `未知状态`，与各页面原有兜底一致——不向一线暴露原始状态码。
 */

const UNKNOWN_STATUS_LABEL = '未知状态'

/** 工单状态可读标签（report / issue / receipt 页面共用，原本三份相同副本）。 */
export const WORK_ORDER_STATUS_LABELS: Record<string, string> = {
  Released: '已下达',
  Planned: '已计划',
  InProgress: '生产中',
  Started: '生产中',
  Completed: '已完成',
  Closed: '已关闭',
  OnHold: '已挂起',
}

export function workOrderStatusLabel(status?: string | null): string {
  return WORK_ORDER_STATUS_LABELS[status ?? ''] ?? UNKNOWN_STATUS_LABEL
}

/** 工序任务状态可读标签（operation / report 页面共用，原本两份相同副本）。 */
export const OPERATION_TASK_STATUS_LABELS: Record<string, string> = {
  Ready: '可开工',
  Running: '执行中',
  Started: '执行中',
  InProgress: '执行中',
  Paused: '已暂停',
  Held: '已暂停',
  ScheduleInvalidated: '排程已失效',
  Completed: '已完成',
  Blocked: '受阻',
}

export function operationTaskStatusLabel(status?: string | null): string {
  return OPERATION_TASK_STATUS_LABELS[status ?? ''] ?? UNKNOWN_STATUS_LABEL
}

/** 领料申请状态可读标签（issue 页面专用状态集，集中存放一处便于维护）。 */
export const MATERIAL_ISSUE_STATUS_LABELS: Record<string, string> = {
  Requested: '待领料',
  Pending: '待领料',
  Issued: '已发料',
  PartiallyReceived: '部分接收',
  Received: '已接收',
  Confirmed: '已接收',
  Completed: '已完成',
  Cancelled: '已取消',
  Rejected: '已驳回',
}

export function materialIssueStatusLabel(status?: string | null): string {
  return MATERIAL_ISSUE_STATUS_LABELS[status ?? ''] ?? UNKNOWN_STATUS_LABEL
}

/** 完工入库申请状态可读标签（receipt 页面专用状态集，集中存放一处便于维护）。 */
export const RECEIPT_STATUS_LABELS: Record<string, string> = {
  Requested: '待入库',
  Pending: '待入库',
  Created: '待入库',
  Submitted: '待入库',
  PartiallyReceived: '部分入库',
  Received: '已入库',
  Completed: '已入库',
  Cancelled: '已取消',
  Rejected: '已驳回',
}

export function receiptStatusLabel(status?: string | null): string {
  return RECEIPT_STATUS_LABELS[status ?? ''] ?? UNKNOWN_STATUS_LABEL
}

/**
 * 工单行的可读标题/副标题（report / issue / receipt 共用，原本三份相同副本）。
 *
 * 仅依赖普通行字段（`workOrderId` / `status` / `skuId` / `quantity`），框架无关。
 */
export interface WorkOrderLabelRow {
  workOrderId?: string | null
  status?: string | null
  skuId?: string | null
  quantity?: number | null
}

export function workOrderTitle(wo: WorkOrderLabelRow): string {
  return wo.workOrderId ?? '无工单'
}

export function workOrderSubtitle(wo: WorkOrderLabelRow): string {
  const parts = [workOrderStatusLabel(wo.status)]
  if (wo.skuId) parts.push(`物料 ${wo.skuId}`)
  if (wo.quantity !== undefined && wo.quantity !== null) parts.push(`计划 ${wo.quantity}`)
  return parts.join(' · ')
}
