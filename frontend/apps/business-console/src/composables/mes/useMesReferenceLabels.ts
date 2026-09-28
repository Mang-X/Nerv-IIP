import type { StatusTone } from '@nerv-iip/ui'

/**
 * MES 各聚合状态码的字面量联合。刻意不从某一条 list query 的生成枚举推导：
 * 旧的 `MesListDisplayOpenApiDocumentProcessor` 把 10 个聚合的状态重填成同一份 29 值并集，
 * 那份并集让本文件服务 6 个聚合的共享词表「碰巧」只有一个 key 类型可写；收敛为按聚合的真实
 * 值域后，再借用单个聚合的枚举做键类型就只剩 10 个工单码，其余全部成为 excess property。
 * 这里的码值与各聚合域常量逐字一致，权威归生产者（#3912）。
 */
export type MesStatusValue =
  // WorkOrder.AllStatuses
  | 'created'
  | 'released'
  | 'started'
  | 'hold'
  | 'completed'
  | 'closed'
  | 'cancelled'
  | 'scrapped'
  | 'split'
  | 'merged'
  // OperationTaskLifecycleStatus
  | 'Queued'
  | 'InProgress'
  | 'Paused'
  | 'ScheduleInvalidated'
  | 'Completed'
  | 'Cancelled'
  // MaterialIssueRequest.*Status
  | 'Requested'
  | 'PartiallyReceived'
  | 'ReceiptPosting'
  | 'Received'
  | 'ReturnRequested'
  | 'ReservationExpired'
  // FinishedGoodsReceiptRequest.*Status
  | 'PartiallyPosted'
  | 'Posted'
  | 'InventoryPostingFailed'
  // DefectRecord.*Status
  | 'Open'
  | 'ReworkPending'
  | 'ScrapAccepted'
  | 'ReturnAccepted'
  | 'DispositionAccepted'
  // ShiftHandover.OpenStatus / AcceptedStatus
  | 'Accepted'
  // WorkCenterUnavailability 读面派生
  | 'Recovered'

export type MesStatusOption = {
  value: 'all' | MesStatusValue
  label: string
}

const statusLabels: Record<MesStatusValue, string> = {
  // WorkOrder（小写）
  created: '已创建',
  released: '已释放',
  started: '已开工',
  hold: '挂起',
  completed: '已完成',
  closed: '已关闭',
  cancelled: '已取消',
  scrapped: '已报废',
  split: '已拆分',
  merged: '已合并',
  // OperationTaskLifecycleStatus（PascalCase）
  Queued: '待开工',
  InProgress: '执行中',
  Paused: '暂停',
  ScheduleInvalidated: '排程已失效',
  Completed: '已完成',
  Cancelled: '已取消',
  // MaterialIssueRequest（PascalCase）
  Requested: '已请求',
  PartiallyReceived: '部分接收',
  ReceiptPosting: '收料过账中',
  Received: '已接收',
  ReturnRequested: '已申请退料',
  ReservationExpired: '预留已过期',
  // FinishedGoodsReceiptRequest（PascalCase）
  PartiallyPosted: '部分入库',
  Posted: '已入库',
  InventoryPostingFailed: '入库失败',
  // DefectRecord（PascalCase）
  Open: '待处理',
  ReworkPending: '返工待处理',
  ScrapAccepted: '报废已受理',
  ReturnAccepted: '退回已受理',
  DispositionAccepted: '处置已受理',
  // ShiftHandover（PascalCase）
  Accepted: '已接班',
  // WorkCenterUnavailability 读面派生（PascalCase）
  Recovered: '已恢复',
}

/**
 * 生成筛选下拉项。
 *
 * `overrides` 用于同一码值在不同业务语境下的说法差异——最典型的是 `open`：
 * 停机/产能语境是「未恢复」，质量语境是「待处理」，交接语境是「待接班」。
 * 共享表只放停机语境的默认值，其余语境在这里显式覆盖，避免筛选项印错词。
 */
function statusOptions(
  values: MesStatusValue[],
  overrides: Partial<Record<MesStatusValue, string>> = {},
): MesStatusOption[] {
  return [
    { value: 'all', label: '全部状态' },
    ...values.map((value) => ({ value, label: overrides[value] ?? statusLabels[value] })),
  ]
}

export const mesWorkOrderStatusOptions = statusOptions([
  'created',
  'released',
  'started',
  'hold',
  'completed',
  'closed',
  'cancelled',
  'scrapped',
])

export const mesProductionPlanStatusOptions = mesWorkOrderStatusOptions

export const mesOperationTaskStatusOptions = statusOptions([
  'Queued',
  'InProgress',
  'Paused',
  'ScheduleInvalidated',
  'Completed',
  'Cancelled',
])

export const mesMaterialIssueStatusOptions = statusOptions([
  'Requested',
  'PartiallyReceived',
  'Received',
])

export const mesQualityStatusOptions = statusOptions([
  'Open',
  'ReworkPending',
  'ScrapAccepted',
  'ReturnAccepted',
  'DispositionAccepted',
])

export const mesReceiptStatusOptions = statusOptions([
  'Requested',
  'PartiallyPosted',
  'Posted',
  'InventoryPostingFailed',
])

// 完工入库状态的可读标签 + 徽章色。与 mesReceiptStatusOptions 同域集中，避免与页面本地映射漂移。
// 键集是 MES 域 `FinishedGoodsReceiptRequest` 的 5 个状态常量（#3912），运行时值即这些常量的
// PascalCase：值从网关 JSON 原样进来（网关 BusinessConsoleMesEndpoints 直传 receipt.ReceiptStatus），
// 前端不改写，因此**不做大小写归一化查表** —— 归一化层会掩盖后端哪天改发小写的契约漂移
// （docs/governance/api/contracts-and-codegen.md：不得用前端临时适配掩盖后端漂移）。
// 入库语境用「已入库」而非通用「已完成」。与 business-core 的 mesLabels 同表，本次不合并。
const RECEIPT_STATUS_LABELS: Record<string, string> = {
  Requested: '待入库',
  PartiallyPosted: '部分入库',
  Posted: '已入库',
  InventoryPostingFailed: '入库失败',
  Cancelled: '已取消',
}
const RECEIPT_STATUS_TONES: Record<string, StatusTone> = {
  Requested: 'neutral',
  PartiallyPosted: 'info',
  Posted: 'success',
  InventoryPostingFailed: 'danger',
  Cancelled: 'neutral',
}
export function receiptStatusLabel(status?: string | null) {
  return RECEIPT_STATUS_LABELS[status ?? ''] ?? '未知状态'
}
export function receiptStatusTone(status?: string | null): StatusTone {
  return RECEIPT_STATUS_TONES[status ?? ''] ?? 'neutral'
}
export function isFailedReceiptStatus(status?: string | null) {
  return status === 'InventoryPostingFailed'
}

export const mesDowntimeStatusOptions = statusOptions(['Open', 'Recovered'])

export const mesCapacityStatusOptions = mesDowntimeStatusOptions

export const mesHandoverStatusOptions = statusOptions(['Open', 'Accepted'])

/**
 * 词表漏词只在开发期告警一次，生产构建里整段被摇树掉。
 * 仍然回吐原值（不让状态列空白），但别让漏词沉默到真机走查才被发现。
 */
const warnedStatusValues = new Set<string>()
function warnMissingStatusLabel(value: string) {
  if (!import.meta.env.DEV) return
  if (warnedStatusValues.has(value)) return
  warnedStatusValues.add(value)
  console.warn(`[MES] 词表缺失: ${value}，请补 useMesReferenceLabels.ts 的状态词表`)
}

/**
 * 共享词表的唯一查表入口。刻意**不做**大小写归一化：命中不到就返回 undefined，
 * 交给调用方兜底，而不是换个拼写再试一次 —— 那层兼容正是 `contracts-and-codegen.md`
 * 「不得用前端临时适配掩盖后端漂移」所禁止的。导出供负向守卫直接断言「查不到」。
 */
export function statusLabelForTest(value: string): string | undefined {
  return statusLabels[value as MesStatusValue]
}

export function useMesReferenceLabels() {
  function statusLabel(value?: string | null) {
    if (!value) return '未知'
    // 直查，不做大小写归一化：见本文件 RECEIPT_STATUS_LABELS 上方的说明。
    const label = statusLabelForTest(value)
    if (label === undefined) warnMissingStatusLabel(value)
    return label ?? value
  }

  function emptyText(value?: string | null) {
    return value && value.trim().length > 0 ? value : '未指定'
  }

  function referenceLabel(display?: string | null, code?: string | null, id?: string | null) {
    return emptyText(display ?? code ?? id)
  }

  return {
    statusLabel,
    emptyText,
    referenceLabel,
  }
}
