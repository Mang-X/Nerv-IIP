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
  Queued: '待开工',
  Ready: '可开工',
  Running: '执行中',
  Started: '执行中',
  InProgress: '执行中',
  Paused: '已暂停',
  Held: '已暂停',
  ScheduleInvalidated: '排程已失效',
  Completed: '已完成',
  Cancelled: '已取消',
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

/**
 * 班次交接单状态可读标签。
 *
 * 取值权威是 MES 域 `ShiftHandover.OpenStatus` / `AcceptedStatus`（`Open` / `Accepted`），
 * 读面按枚举名回显字符串。`open` 在交接语境是「待接班」而不是通用的「待处理」。
 */
export const SHIFT_HANDOVER_STATUS_LABELS: Record<string, string> = {
  open: '待接班',
  accepted: '已接班',
}

export function shiftHandoverStatusLabel(status?: string | null): string {
  return SHIFT_HANDOVER_STATUS_LABELS[normalizeHandoverCode(status)] ?? UNKNOWN_STATUS_LABEL
}

/**
 * 遗留问题来源域。取值权威是 MES 域枚举 `ShiftHandoverIssueCategory`（Equipment / Quality）；
 * 写面按 `ShiftHandoverVocabulary.ParseCategory` 大小写不敏感解析，因此这里按小写归一后查表。
 */
export const SHIFT_HANDOVER_ISSUE_CATEGORY_LABELS: Record<string, string> = {
  equipment: '设备',
  quality: '质量',
}

/** 写面可提交的类别码（与域枚举同名，写面收字符串）。 */
export const SHIFT_HANDOVER_ISSUE_CATEGORY_CODES = ['Equipment', 'Quality'] as const

export function shiftHandoverIssueCategoryLabel(category?: string | null): string {
  return SHIFT_HANDOVER_ISSUE_CATEGORY_LABELS[normalizeHandoverCode(category)] ?? '未分类'
}

/** 遗留问题严重度。取值权威是 MES 域枚举 `ShiftHandoverIssueSeverity`（Low / Medium / High）。 */
export const SHIFT_HANDOVER_ISSUE_SEVERITY_LABELS: Record<string, string> = {
  low: '低',
  medium: '中',
  high: '高',
}

/** 写面可提交的严重度码（与域枚举同名，写面收字符串）。 */
export const SHIFT_HANDOVER_ISSUE_SEVERITY_CODES = ['Low', 'Medium', 'High'] as const

/**
 * 解不出时用 `未知级别`，与本模块 `alarmSeverityLabel` 的既有口径一致。
 *
 * console 侧那份副本用的是 `未定级`、本文件早先写的是 `未分级`——**两个都是离群值**，
 * 同一 business-core 里已经有一个严重度回落文案在用 `未知级别`。副本收拢（#3470）以这份为准。
 */
export function shiftHandoverIssueSeverityLabel(severity?: string | null): string {
  return SHIFT_HANDOVER_ISSUE_SEVERITY_LABELS[normalizeHandoverCode(severity)] ?? '未知级别'
}

/**
 * 未完工单状态的**写面值域** —— 与读面展示表 `WORK_ORDER_STATUS_LABELS` 是两件事。
 *
 * 读面表回答「拿到这个码怎么显示」，所以它是历史拼写的并集，里面既有 `InProgress` 又有
 * `Started`（两者都显示「生产中」），也含 `Completed` / `Closed`。把它当选择器数据源有两个
 * 后果：屏上并排两条无法区分的「生产中」；以及允许把一张**未完工单**标成「已完成/已关闭」
 * ——那与「未完工单」的定义（`completedQuantity < plannedQuantity`）自相矛盾。
 *
 * 所以写面单独定义：
 * - 只留仍在流转的状态，终态（completed / closed / cancelled / scrapped / split / merged）一律排除；
 * - 「生产中」只保留 `Started` 一个码——MES 域常量是 `WorkOrder.StartedStatus`，它是更贴近域的拼写。
 */
export const SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS = [
  { code: 'Planned', label: '已计划' },
  { code: 'Released', label: '已下达' },
  { code: 'Started', label: '生产中' },
  { code: 'OnHold', label: '已挂起' },
] as const

function normalizeHandoverCode(value?: string | null): string {
  return (value ?? '').trim().toLowerCase()
}
