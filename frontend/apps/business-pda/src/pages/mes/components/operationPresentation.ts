import type { BusinessConsoleMesOperationTaskRow } from '@nerv-iip/api-client'
import { operationTaskStatusLabel } from '@nerv-iip/business-core'

export type OperationActionKind = 'start' | 'pause' | 'resume' | 'complete'

export type OperationResultState = {
  status: 'success' | 'error'
  title: string
  description?: string
  action: OperationActionKind
  displayReference: string
  workOrderId: string
  taskId: string
  contextIdentity: string
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
  if (!task?.allowedActions) return []
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
  return sequence ? `${workOrder} · ${sequence}` : workOrder
}

export function operationTaskRowSubtitle(task: BusinessConsoleMesOperationTaskRow) {
  const parts = [operationTaskStatusLabel(task.status)]
  if (task.workCenterId) parts.push(`工作中心 ${task.workCenterId}`)
  if (task.operationCode) parts.push(`工序 ${task.operationCode}`)
  if (task.assignedUserName) parts.push(`受派 ${task.assignedUserName}`)
  return parts.join(' · ')
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
