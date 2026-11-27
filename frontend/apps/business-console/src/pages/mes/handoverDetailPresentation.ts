import type {
  BusinessConsoleMesShiftHandoverOpenIssue,
  BusinessConsoleMesShiftHandoverUnfinishedWorkOrder,
  BusinessConsoleMesShiftHandoverWipItem,
} from '@nerv-iip/api-client'
import {
  incomingPartyState,
  outgoingPartyState,
  SHIFT_HANDOVER_PARTY_STATE_LABELS,
} from '@nerv-iip/business-core'
import type { NvDataTableColumn, StatusTone } from '@nerv-iip/ui'
import { labelFor, normalizeCode, MES_HANDOVER_ISSUE_CATEGORY_LABELS } from '@/data/businessLabels'

function reliablePartyName(name?: string | null, id?: string | null) {
  const value = name?.trim()
  return value && value !== id?.trim() && !/^[\da-f]{8}(?:-[\da-f]{4}){3}-[\da-f]{12}$/i.test(value)
    ? value
    : undefined
}

export function outgoingUserLabel(row: Parameters<typeof outgoingPartyState>[0]) {
  const name = reliablePartyName(row.outgoingUserName, row.outgoingUserId)
  const state = outgoingPartyState({ ...row, outgoingUserName: name })
  return name ?? partyStateLabel(state)
}

export function incomingUserLabel(row: Parameters<typeof incomingPartyState>[0]) {
  const name = reliablePartyName(row.incomingUserName, row.incomingUserId)
  const state = incomingPartyState({ ...row, incomingUserName: name })
  return name ?? partyStateLabel(state)
}

function partyStateLabel(state: keyof typeof SHIFT_HANDOVER_PARTY_STATE_LABELS) {
  return state === 'name-unresolved'
    ? '人员已记录，姓名未显示'
    : SHIFT_HANDOVER_PARTY_STATE_LABELS[state]
}

const ISSUE_SEVERITY_TONES: Readonly<Record<string, StatusTone>> = {
  low: 'neutral',
  medium: 'warning',
  high: 'danger',
}
export function issueSeverityTone(value?: string | null): StatusTone {
  return ISSUE_SEVERITY_TONES[normalizeCode(value)] ?? 'neutral'
}

export const wipColumns: NvDataTableColumn<BusinessConsoleMesShiftHandoverWipItem>[] = [
  { key: 'workOrderId', header: '工单', cellClass: 'font-medium' },
  {
    key: 'operationTaskId',
    header: '工序任务',
    accessor: (row) => row.operationTaskId?.trim() || '按工单登记',
  },
  { key: 'quantity', header: '在制数量', align: 'end', width: 'w-28' },
]

export const unfinishedWorkOrderColumns: NvDataTableColumn<BusinessConsoleMesShiftHandoverUnfinishedWorkOrder>[] =
  [
    { key: 'workOrderId', header: '工单', cellClass: 'font-medium' },
    { key: 'plannedQuantity', header: '计划数量', align: 'end', width: 'w-24' },
    { key: 'completedQuantity', header: '完成数量', align: 'end', width: 'w-24' },
    { key: 'workOrderStatus', header: '工单状态', width: 'w-28' },
  ]

export const openIssueColumns: NvDataTableColumn<BusinessConsoleMesShiftHandoverOpenIssue>[] = [
  {
    key: 'category',
    header: '类别',
    width: 'w-20',
    accessor: (row) => labelFor(MES_HANDOVER_ISSUE_CATEGORY_LABELS, row.category, '未分类'),
  },
  { key: 'severity', header: '严重度', width: 'w-24' },
  { key: 'description', header: '问题描述' },
  { key: 'referenceId', header: '关联单据', accessor: (row) => row.referenceId?.trim() || '无' },
]

/**
 * 三类明细的读面都没有 id 字段（WipItem / UnfinishedWorkOrder / OpenIssue 只有业务属性），
 * 拿属性拼键就会撞——同类别同描述的两条遗留问题、同工单同工序的两条在制清点都是合法数据。
 *
 * 这里查的是**对象引用**在本数组里的位置，成立条件只有一条：同一数组内各元素引用互不相同
 * （读面反序列化出来的对象天然满足）。所以给某列加排序也不必重做这个键——`NvDataTable`
 * 排序后透传的仍是同一批对象引用。真正会坏的是换成「渲染时的下标」那类与数据脱钩的写法。
 */
export function rowPositionKey<T>(rows: readonly T[]) {
  return (row: T) => String(rows.indexOf(row))
}
