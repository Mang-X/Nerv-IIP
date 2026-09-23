import type {
  BusinessConsoleMesReceiptRequestRow,
  ListBusinessConsoleMesWorkOrdersData,
} from '@nerv-iip/api-client'
import type { StatusTone } from '@nerv-iip/ui'

type MesStatusValue = NonNullable<
  NonNullable<ListBusinessConsoleMesWorkOrdersData['query']>['status']
>

export type MesStatusOption = {
  value: 'all' | MesStatusValue
  label: string
}

const statusLabels: Record<MesStatusValue, string> = {
  accepted: '已受理',
  active: '生效中',
  blocked: '阻塞',
  cancelled: '已取消',
  closed: '已关闭',
  completed: '已完成',
  created: '已创建',
  dispositionAccepted: '处置已受理',
  hold: '挂起',
  inProgress: '执行中',
  inventoryPostingFailed: '入库失败',
  open: '未恢复',
  partiallyPosted: '部分入库',
  partiallyReceived: '部分接收',
  paused: '暂停',
  posted: '已入库',
  queued: '待开工',
  ready: '就绪',
  received: '已接收',
  recovered: '已恢复',
  released: '已释放',
  returnAccepted: '退回已受理',
  reworkPending: '返工待处理',
  scrapAccepted: '报废已受理',
  scrapped: '已报废',
  requested: '已请求',
  scheduleInvalidated: '排程已失效',
  started: '已开工',
  warning: '预警',
}

const normalizedStatusLabels = Object.fromEntries(
  Object.entries(statusLabels).flatMap(([key, label]) => [
    [key, label],
    [key.charAt(0).toUpperCase() + key.slice(1), label],
    [key.toLowerCase(), label],
  ]),
)

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
  'queued',
  'scheduleInvalidated',
  'inProgress',
  'paused',
  'completed',
  'cancelled',
])

export const mesMaterialIssueStatusOptions = statusOptions([
  'requested',
  'partiallyReceived',
  'received',
])

export const mesQualityStatusOptions = statusOptions(
  ['open', 'reworkPending', 'scrapAccepted', 'returnAccepted', 'dispositionAccepted'],
  { open: '待处理' },
)

export const mesReceiptStatusOptions = statusOptions([
  'requested',
  'partiallyPosted',
  'posted',
  'inventoryPostingFailed',
])

// 完工入库状态的可读标签 + 徽章色。与 mesReceiptStatusOptions 同域集中，避免与页面本地映射漂移。
// 运行时 receiptStatus 为原始 PascalCase 域状态（Requested/PartiallyPosted/Posted/InventoryPostingFailed/Cancelled），
// 入库语境用「已入库」而非通用「已完成」；大小写不敏感查表。
const RECEIPT_STATUS_LABELS: Record<string, string> = {
  requested: '待入库',
  partiallyposted: '部分入库',
  posted: '已入库',
  inventorypostingfailed: '入库失败',
  cancelled: '已取消',
}
const RECEIPT_STATUS_TONES: Record<string, StatusTone> = {
  requested: 'neutral',
  partiallyposted: 'info',
  posted: 'success',
  inventorypostingfailed: 'danger',
  cancelled: 'neutral',
}
function normalizeReceiptStatus(status?: string | null) {
  return (status ?? '').toLowerCase()
}
export function receiptStatusLabel(status?: string | null) {
  return RECEIPT_STATUS_LABELS[normalizeReceiptStatus(status)] ?? '未知状态'
}
export function receiptStatusTone(status?: string | null): StatusTone {
  return RECEIPT_STATUS_TONES[normalizeReceiptStatus(status)] ?? 'neutral'
}
export function isFailedReceiptStatus(status?: string | null) {
  return normalizeReceiptStatus(status) === 'inventorypostingfailed'
}

// 「待入库」卡在哪一环（#3728）。Requested 是两段等待：先等 ERP 成本归集回传单位成本，
// 拿到单位成本后才提交库存过账。成本归集卡住时 MES 没有失败码，所以每个待入库行都要在这里给出原因；
// costCapitalization 由网关按调用者 ERP 财务读权限附带，没有时只能说明在等哪一环。
export function receiptPendingReason(
  row: Pick<
    BusinessConsoleMesReceiptRequestRow,
    'receiptStatus' | 'unitCost' | 'costCapitalization'
  >,
): string | null {
  if (normalizeReceiptStatus(row.receiptStatus) !== 'requested') return null
  if (row.unitCost !== undefined && row.unitCost !== null) return '已提交库存过账，等待库存确认'
  const progress = row.costCapitalization
  if (!progress) return '等待 ERP 成本归集回传单位成本'
  if (progress.capitalizationPublished) return 'ERP 已完成成本归集，等待单位成本回传'
  // 归集停住（完工或报工事件被 ERP 拒绝）只有管理员能在死信里处理，业务用户能做的是找管理员。
  const escalate = '长时间不变请联系系统管理员'
  if (!progress.workOrderCompleted) return `等待 ERP 收到工单完工后归集成本；${escalate}`
  const counts = `报工成本 ${progress.receivedReportCount ?? 0}/${progress.expectedReportCount ?? 0}，物料过账 ${progress.receivedMaterialMovementCount ?? 0}/${progress.expectedMaterialMovementCount ?? 0}`
  return `等待 ERP 成本归集（${counts}）；${escalate}`
}

export const mesDowntimeStatusOptions = statusOptions(['open', 'recovered'])

export const mesCapacityStatusOptions = mesDowntimeStatusOptions

export const mesHandoverStatusOptions = statusOptions(['open', 'accepted'], { open: '待接班' })

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
    const label = normalizedStatusLabels[value] ?? normalizedStatusLabels[value.toLowerCase()]
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
