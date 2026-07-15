import type { ListBusinessConsoleMesWorkOrdersData } from '@nerv-iip/api-client'

type MesStatusValue = NonNullable<NonNullable<ListBusinessConsoleMesWorkOrdersData['query']>['status']>

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

function statusOptions(values: MesStatusValue[]): MesStatusOption[] {
  return [
    { value: 'all', label: '全部状态' },
    ...values.map((value) => ({ value, label: statusLabels[value] })),
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

export const mesQualityStatusOptions = statusOptions([
  'open',
  'reworkPending',
  'scrapAccepted',
  'returnAccepted',
  'dispositionAccepted',
])

export const mesReceiptStatusOptions = statusOptions([
  'requested',
  'partiallyPosted',
  'posted',
  'inventoryPostingFailed',
])

export const mesDowntimeStatusOptions = statusOptions([
  'open',
  'recovered',
])

export const mesCapacityStatusOptions = mesDowntimeStatusOptions

export const mesHandoverStatusOptions = statusOptions([
  'open',
  'accepted',
])

export function useMesReferenceLabels() {
  function statusLabel(value?: string | null) {
    if (!value) return '未知'
    return normalizedStatusLabels[value] ?? normalizedStatusLabels[value.toLowerCase()] ?? value
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
