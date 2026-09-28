import type { StatusTone } from '@nerv-iip/ui'
import type {
  GetBusinessConsoleMesWipSummaryData,
  ListBusinessConsoleMesCapacityImpactsData,
  ListBusinessConsoleMesDispatchTasksData,
  ListBusinessConsoleMesDowntimeEventsData,
  ListBusinessConsoleMesFinishedGoodsReceiptRequestsData,
  ListBusinessConsoleMesMaterialIssueRequestsData,
  ListBusinessConsoleMesOperationTasksData,
  ListBusinessConsoleMesRelatedQualityItemsData,
  ListBusinessConsoleMesShiftHandoversData,
  ListBusinessConsoleMesWorkOrdersData,
} from '@nerv-iip/api-client'

/**
 * 取某条 list/query 生成类型里 `status` 的真值域。约束成带 query 的形状，
 * 该路径不带 status 时结果是 `undefined`（即联合里没有这一项）。
 */
type StatusOf<T extends { query?: unknown }> = T['query'] extends { status?: infer S }
  ? NonNullable<S>
  : never

/**
 * MES 各聚合状态码的字面量联合 = 各聚合生成 query 类型的 `status` 联合之并。
 *
 * 逐个聚合从 api-client 生成类型取，而不是手抄：生成类型由 Gateway 契约派生，
 * 契约收敛到真实值域后（#3912）每条路径的枚举都已经是该聚合自己的值域。后端给某个
 * 聚合增删一个状态，重新生成的类型会立刻带上变化，本联合随之变化；手抄则要靠人记得同步。
 *
 * 取并集而非某一个聚合的联合，是因为这份联合要当**共享词表**的键类型 —— 词表服务 6 个
 * 聚合，任一聚合的码都必须是合法键。旧实现借 WorkOrder 一家的枚举当键类型，其余 5 个
 * 聚合的码全是 excess property，那正是 21 个 typecheck 错误的来源。
 */
export type MesStatusValue =
  | StatusOf<ListBusinessConsoleMesWorkOrdersData>
  | StatusOf<ListBusinessConsoleMesOperationTasksData>
  | StatusOf<GetBusinessConsoleMesWipSummaryData>
  | StatusOf<ListBusinessConsoleMesDispatchTasksData>
  | StatusOf<ListBusinessConsoleMesMaterialIssueRequestsData>
  | StatusOf<ListBusinessConsoleMesFinishedGoodsReceiptRequestsData>
  | StatusOf<ListBusinessConsoleMesRelatedQualityItemsData>
  | StatusOf<ListBusinessConsoleMesShiftHandoversData>
  | StatusOf<ListBusinessConsoleMesDowntimeEventsData>
  | StatusOf<ListBusinessConsoleMesCapacityImpactsData>

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
  // DefectRecord（PascalCase）。`Open` 是跨聚合重码：不良记录=待处理、交接=待接班、
  // 停机/产能=未恢复。共享表取停机语境的读法（与 base 一致），其余语境在
  // statusOptions 的 overrides 里显式覆盖 —— 漏覆盖就会印错词，所以那三个调用点
  // 各有断言钉住（见 useMesReferenceLabels.test.ts）。
  Open: '未恢复',
  ReworkPending: '返工待处理',
  ScrapAccepted: '报废已受理',
  ReturnAccepted: '退回已受理',
  DispositionAccepted: '处置已受理',
  // ShiftHandover（PascalCase）。`Accepted` 同样是重码：交接=已接班，不良记录
  // 三个 *Accepted=已受理。停机/产能的值域里没有它，故此处取交接读法。
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

export const mesQualityStatusOptions = statusOptions(
  ['Open', 'ReworkPending', 'ScrapAccepted', 'ReturnAccepted', 'DispositionAccepted'],
  // 不良记录语境的 `Open` 是「待处理」，不是停机的「未恢复」。
  { Open: '待处理' },
)

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

export const mesHandoverStatusOptions = statusOptions(['Open', 'Accepted'], {
  // 交接语境的 `Open` 是「待接班」。
  Open: '待接班',
})

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

export function useMesReferenceLabels() {
  function statusLabel(value?: string | null) {
    if (!value) return '未知'
    // 直查，不做大小写归一化：见本文件 RECEIPT_STATUS_LABELS 上方的说明。
    // 查不到就回吐原值并告警，而不是换个拼写再试一次 —— 那层兼容正是
    // `contracts-and-codegen.md`「不得用前端临时适配掩盖后端漂移」所禁止的。
    // 刻意不导出只读表入口给测试另开一条路：守卫必须打在这条生产渲染路径上，
    // 否则有人把归一化加回这里而测试仍绿，正确性就成了实现巧合而非断言保证。
    const label = statusLabels[value as MesStatusValue]
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
