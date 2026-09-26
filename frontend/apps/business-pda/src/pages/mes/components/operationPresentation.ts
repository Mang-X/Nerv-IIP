import type { BusinessConsoleMesOperationTaskRow } from '@nerv-iip/api-client'
import { operationTaskStatusLabel } from '@nerv-iip/business-core'
import type { OperationActionContext } from '@/composables/useBusinessMes'
import {
  hasCompleteReworkAuthority,
  isReworkWorkOrder,
  parseMesWorkOrderAuthority,
  type MesWorkOrderAuthority,
} from '@/composables/mes/mesWorkOrderAuthority'

export type OperationActionKind = 'start' | 'pause' | 'resume' | 'complete'

export type OperationResultState = {
  status: 'success' | 'error'
  title: string
  description?: string
  action: OperationActionKind
  displayReference: string
  workOrderId: string
  taskId: string
  context: OperationActionContext
}

const recognizedActions = new Set<OperationActionKind>(['start', 'pause', 'resume', 'complete'])

export const OPERATION_ACTION_LABELS: Record<OperationActionKind, string> = {
  start: '开始',
  pause: '暂停',
  resume: '恢复',
  complete: '完成',
}

export const OPERATION_SUCCESS_TITLES: Record<OperationActionKind, string> = {
  start: '工序已开始',
  pause: '工序已暂停',
  resume: '工序已恢复',
  complete: '工序已完成',
}

export function actionsForOperationTask(
  task: BusinessConsoleMesOperationTaskRow | null,
): OperationActionKind[] {
  if (!task?.allowedActions || !hasCompleteReworkAuthority(task)) return []
  return task.allowedActions.flatMap((value) => {
    const normalized = value.trim().toLowerCase() as OperationActionKind
    return recognizedActions.has(normalized) ? [normalized] : []
  })
}

export function workOrderLabel(task: BusinessConsoleMesOperationTaskRow) {
  return task.workOrderNo?.trim() || '工单信息未提供'
}

export function operationTaskLabel(task: BusinessConsoleMesOperationTaskRow) {
  return task.operationTaskNo?.trim() || '工序任务信息未提供'
}

export function deviceLabel(task: BusinessConsoleMesOperationTaskRow) {
  const name = task.deviceAssetName?.trim()
  const code = task.deviceAssetCode?.trim()
  if (name && code) return `${name}（${code}）`
  return name || code || '设备信息未提供'
}

export function taskDisplayReference(task: BusinessConsoleMesOperationTaskRow) {
  return `${workOrderLabel(task)} · ${operationTaskLabel(task)}`
}

export function operationTaskRowTitle(task: BusinessConsoleMesOperationTaskRow) {
  const sequence = task.operationSequence === undefined ? '' : `工序 ${task.operationSequence}`
  const workOrder = workOrderLabel(task)
  return withReworkLabel(sequence ? `${workOrder} · ${sequence}` : workOrder, task)
}

export function operationTaskRowSubtitle(task: BusinessConsoleMesOperationTaskRow) {
  const parts = [operationTaskStatusLabel(task.status)]
  if (task.workCenterId) parts.push(`工作中心 ${task.workCenterId}`)
  if (task.operationCode) parts.push(`工序 ${task.operationCode}`)
  if (task.assignedUserName) parts.push(`受派 ${task.assignedUserName}`)
  const source = reworkSourceLabel(task)
  if (source) parts.push(source)
  return parts.join(' · ')
}

export function withReworkLabel(label: string, item: MesWorkOrderAuthority) {
  return isReworkWorkOrder(item) && hasCompleteReworkAuthority(item) ? `返工 · ${label}` : label
}

export function reworkSourceLabel(item: MesWorkOrderAuthority) {
  const authority = parseMesWorkOrderAuthority(item)
  if (authority?.kind !== 'rework') return ''
  return `来源 NCR ${authority.sourceNcrCode} · 源工单 ${authority.sourceWorkOrderId}`
}

export function formatOperationDate(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString()
}

export function formatOperationDateTime(value?: string | null) {
  if (!value) return '未提供'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN', { hour12: false })
}

const TELEMETRY_CANDIDATE_SUSPENSION_LABELS: Readonly<Record<string, string>> = {
  'active-alarm': '设备存在未处理报警',
  'no-work-center-mapping': '设备未绑定工作中心',
  'no-current-work-order': '工作中心当前无在制工单',
}

const TELEMETRY_CANDIDATE_STATUS_LABELS: Readonly<Record<string, string>> = {
  draft: '草稿',
  'pending-confirmation': '待确认',
  confirmed: '已确认',
  dismissed: '已忽略',
}

/** 遥测报工候选的状态说明：有挂起原因时说原因，否则说状态；不认识的码不上屏。 */
export function telemetryCandidateStateLabel(candidate: {
  status?: string | null
  suspensionReason?: string | null
}) {
  const reason = candidate.suspensionReason?.trim().toLowerCase()
  if (reason) return TELEMETRY_CANDIDATE_SUSPENSION_LABELS[reason] ?? ''
  return TELEMETRY_CANDIDATE_STATUS_LABELS[candidate.status?.trim().toLowerCase() ?? ''] ?? ''
}
